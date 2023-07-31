﻿using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Polly;

namespace Microsoft.OneFuzz.Service;

public interface IAdo {
    public Async.Task<OneFuzzResultVoid> NotifyAdo(AdoTemplate config, Container container, IReport reportable, bool isLastRetryAttempt, Guid notificationId);
}

public class Ado : NotificationsBase, IAdo {
    public Ado(ILogger<Ado> logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task<OneFuzzResultVoid> NotifyAdo(AdoTemplate config, Container container, IReport reportable, bool isLastRetryAttempt, Guid notificationId) {
        var filename = reportable.FileName();
        Report? report;
        if (reportable is RegressionReport regressionReport) {
            if (regressionReport.CrashTestResult.CrashReport is not null) {
                report = regressionReport.CrashTestResult.CrashReport with {
                    InputBlob = regressionReport.OriginalCrashTestResult?.CrashReport?.InputBlob ??
                                regressionReport.OriginalCrashTestResult?.NoReproReport?.InputBlob
                };
                _logTracer.LogInformation("parsing regression report for ado integration. container:{Container} filename:{Filename}", container, filename);
            } else {
                _logTracer.LogError("ado integration does not support this regression report. container:{Container} filename:{Filename}", container, filename);
                return OneFuzzResultVoid.Ok;
            }
        } else {
            report = (Report)reportable;
        }

        var notificationInfo = new List<(string, string)> {
            ("notification_id", notificationId.ToString()),
            ("job_id", report.JobId.ToString()),
            ("task_id", report.TaskId.ToString()),
            ("ado_project", config.Project),
            ("ado_url", config.BaseUrl.ToString()),
            ("container", container.String),
            ("filename", filename)
        };

        var adoEventType = "AdoNotify";
        _logTracer.AddTags(notificationInfo);
        _logTracer.LogEvent(adoEventType);

        try {
            var ado = await AdoConnector.AdoConnectorCreator(_context, container, filename, config, report, _logTracer);
            await ado.Process(notificationInfo);
        } catch (Exception e)
              when (e is VssUnauthorizedException || e is VssAuthenticationException || e is VssServiceException) {
            if (config.AdoFields.TryGetValue("System.AssignedTo", out var assignedTo)) {
                _logTracer.AddTag("assigned_to", assignedTo);
            }

            if (!isLastRetryAttempt && IsTransient(e)) {
                _logTracer.LogError("transient ADO notification failure {JobId} {TaskId} {Container} {Filename}", report.JobId, report.TaskId, container, filename);
                throw;
            } else {
                _logTracer.LogError(e, "Failed to process ado notification");
                await LogFailedNotification(report, e, notificationId);
                return OneFuzzResultVoid.Error(ErrorCode.NOTIFICATION_FAILURE,
                    $"Failed to process ado notification : exception: {e}");
            }
        }
        return OneFuzzResultVoid.Ok;
    }

    private static bool IsTransient(Exception e) {
        var errorCodes = new List<string>()
        {
            //TF401349: An unexpected error has occurred, please verify your request and try again.
            "TF401349",
            //TF26071: This work item has been changed by someone else since you opened it. You will need to refresh it and discard your changes.
            "TF26071",
        };

        var errorStr = e.ToString();
        return errorCodes.Any(code => errorStr.Contains(code));
    }

    public static async Async.Task<OneFuzzResultVoid> Validate(AdoTemplate config) {
        // Validate PAT is valid for the base url
        VssConnection connection;
        if (config.AuthToken.Secret is SecretValue<string> token) {
            var policy = Policy.Handle<HttpRequestException>().WaitAndRetryAsync(3, _ => new TimeSpan(0, 0, 5));
            try {
                connection = new VssConnection(config.BaseUrl, new VssBasicCredential(string.Empty, token.Value));
                await policy.ExecuteAsync(async () => {
                    await connection.ConnectAsync();
                });
            } catch (HttpRequestException e) {
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_UNEXPECTED_HTTP_EXCEPTION, new string[] {
                    $"Failed to connect to {config.BaseUrl} due to an HttpRequestException",
                    $"Exception: {e}"
                });
            } catch (VssUnauthorizedException e) {
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PAT, new string[] {
                    $"Failed to connect to {config.BaseUrl} using the provided token",
                    $"Exception: {e}"
                });
            } catch (VssAuthenticationException e) {
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PAT, new string[] {
                    $"Failed to connect to {config.BaseUrl} using the provided token",
                    $"Exception: {e}"
                });
            } catch (Exception e) {
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_UNEXPECTED_ERROR, new string[] {
                    $"Unexpected failure when connecting to {config.BaseUrl}",
                    $"Exception: {e}"
                });
            }
        } else {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PAT, "Auth token is missing or invalid");
        }

        try {
            // Validate unique_fields are part of the project's valid fields
            var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>();

            // The set of valid fields for this project according to ADO
            var projectValidFields = await GetValidFields(witClient, config.Project);

            var configFields = config.UniqueFields.Select(field => field.ToLowerInvariant()).ToHashSet();
            var validConfigFields = configFields.Intersect(projectValidFields.Keys).ToHashSet();

            if (!validConfigFields.SetEquals(configFields)) {
                var invalidFields = configFields.Except(validConfigFields);
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_FIELDS, new[]
                    {
                        $"The following unique fields are not valid fields for this project: {string.Join(',', invalidFields)}",
                        "You can find the valid fields for your project by following these steps: https://learn.microsoft.com/en-us/azure/devops/boards/work-items/work-item-fields?view=azure-devops#review-fields"
                    }
                );
            }
        } catch (VssUnauthorizedException e) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_MISSING_PAT_SCOPES, new string[] {
                "The provided PAT may be missing scopes. We were able to connect with it but unable to validate the fields.",
                "Please check the configured scopes.",
                $"Exception: {e}"
            });
        } catch (VssAuthenticationException e) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_MISSING_PAT_SCOPES, new string[] {
                "The provided PAT may be missing scopes. We were able to connect with it but unable to validate the fields.",
                "Please check the configured scopes.",
                $"Exception: {e}"
            });
        } catch (Exception e) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_UNEXPECTED_ERROR, new string[] {
                "Failed to query and compare the valid fields for this project",
                $"Exception: {e}"
            });
        }

        return OneFuzzResultVoid.Ok;
    }

    private static WorkItemTrackingHttpClient GetAdoClient(Uri baseUrl, string token) {
        return new WorkItemTrackingHttpClient(baseUrl, new VssBasicCredential("PAT", token));
    }

    private static async Async.Task<Dictionary<string, WorkItemField2>> GetValidFields(WorkItemTrackingHttpClient client, string? project) {
        return (await client.GetWorkItemFieldsAsync(project, expand: GetFieldsExpand.ExtensionFields))
            .ToDictionary(field => field.ReferenceName.ToLowerInvariant());
    }

    public sealed class AdoConnector {
        // https://github.com/MicrosoftDocs/azure-devops-docs/issues/5890#issuecomment-539632059
        private const int MAX_SYSTEM_TITLE_LENGTH = 128;
        private const string TITLE_FIELD = "System.Title";
        private readonly RenderedAdoTemplate _config;
        private readonly string _project;
        private readonly WorkItemTrackingHttpClient _client;
        private readonly Uri _instanceUrl;
        private readonly ILogger _logTracer;
        public static async Async.Task<AdoConnector> AdoConnectorCreator(IOnefuzzContext context, Container container, string filename, AdoTemplate config, Report report, ILogger logTracer, Renderer? renderer = null) {
            if (!config.AdoFields.TryGetValue(TITLE_FIELD, out var issueTitle)) {
                issueTitle = "{{ report.crash_site }} - {{ report.executable }}";
            }
            var instanceUrl = context.Creds.GetInstanceUrl();
            renderer ??= await Renderer.ConstructRenderer(context, container, filename, issueTitle, report, instanceUrl, logTracer);
            var project = renderer.Render(config.Project, instanceUrl);

            var authToken = await context.SecretsOperations.GetSecretValue(config.AuthToken.Secret);
            var client = GetAdoClient(config.BaseUrl, authToken!);

            // TODO: Fix strict rendering
            var renderedConfig = _renderedAdoTemplate(logTracer, renderer, config, instanceUrl, true);
            return new AdoConnector(renderedConfig, project!, client, instanceUrl, logTracer);
        }

        private static RenderedAdoTemplate _renderedAdoTemplate(ILogger logTracer, Renderer renderer, AdoTemplate original, Uri instanceUrl, bool strictRendering) {
            var adoFields = original.AdoFields.ToDictionary(kvp => kvp.Key, kvp => renderer.Render(kvp.Value, instanceUrl, strictRendering));
            var onDuplicateAdoFields = original.OnDuplicate.AdoFields.ToDictionary(kvp => kvp.Key, kvp => renderer.Render(kvp.Value, instanceUrl, strictRendering));

            var systemTitle = renderer.IssueTitle;
            if (systemTitle.Length > MAX_SYSTEM_TITLE_LENGTH) {
                var systemTitleHashString = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(systemTitle))
                );
                // try to avoid naming collisions caused by the trim by appending the first 8 characters of the title's hash at the end
                var truncatedTitle = $"{systemTitle[..(MAX_SYSTEM_TITLE_LENGTH - 14)]}... [{systemTitleHashString[..8]}]";

                // TITLE_FIELD is required in adoFields (ADO won't allow you to create a work item without a title)
                adoFields[TITLE_FIELD] = truncatedTitle;

                // It may or may not be present in on_duplicate
                if (onDuplicateAdoFields.ContainsKey(TITLE_FIELD)) {
                    onDuplicateAdoFields[TITLE_FIELD] = truncatedTitle;
                }

                logTracer.LogInformation(
                    "System.Title \"{Title}\" was too long ({TitleLength} chars); shortend it to \"{NewTitle}\" ({NewTitleLength} chars)",
                    systemTitle,
                    systemTitle.Length,
                    adoFields[TITLE_FIELD],
                    adoFields[TITLE_FIELD].Length
                );
            }

            var onDuplicateUnless = original.OnDuplicate.Unless?.Select(dict =>
                    dict.ToDictionary(kvp => kvp.Key, kvp => renderer.Render(kvp.Value, instanceUrl, strictRendering)))
                    .ToList();

            var onDuplicate = new ADODuplicateTemplate(
                original.OnDuplicate.Increment,
                original.OnDuplicate.SetState,
                onDuplicateAdoFields,
                original.OnDuplicate.Comment != null ? renderer.Render(original.OnDuplicate.Comment, instanceUrl, strictRendering) : null,
                onDuplicateUnless
            );

            return new RenderedAdoTemplate(
                original.BaseUrl,
                original.AuthToken,
                renderer.Render(original.Project, instanceUrl, strictRendering),
                renderer.Render(original.Type, instanceUrl, strictRendering),
                original.UniqueFields,
                adoFields,
                onDuplicate,
                original.Comment != null ? renderer.Render(original.Comment, instanceUrl, strictRendering) : null
            );
        }


        public AdoConnector(RenderedAdoTemplate config, string project, WorkItemTrackingHttpClient client, Uri instanceUrl, ILogger logTracer) {
            _config = config;
            _project = project;
            _client = client;
            _instanceUrl = instanceUrl;
            _logTracer = logTracer;
        }

        public async IAsyncEnumerable<WorkItem> ExistingWorkItems(IList<(string, string)> notificationInfo) {
            var filters = new Dictionary<string, string>();
            foreach (var key in _config.UniqueFields) {
                var filter = string.Empty;
                if (string.Equals("System.TeamProject", key)) {
                    filter = _config.Project;
                } else if (_config.AdoFields.TryGetValue(key, out var field)) {
                    filter = field;
                } else {
                    _logTracer.AddTags(notificationInfo);
                    _logTracer.LogError("Failed to check for existing work items using the UniqueField Key: {Key}. Value is not present in config field AdoFields.", key);
                    continue;
                }

                filters.Add(key.ToLowerInvariant(), filter);
            }

            var project = filters.TryGetValue("system.teamproject", out var value) ? value : null;
            var validFields = await GetValidFields(_client, project);

            var postQueryFilter = new Dictionary<string, string>();
            /*
            # WIQL (Work Item Query Language) is an SQL like query language that
            # doesn't support query params, safe quoting, or any other SQL-injection
            # protection mechanisms.
            #
            # As such, build the WIQL with a those fields we can pre-determine are
            # "safe" and otherwise use post-query filtering.
            */

            var parts = new List<string>();
            foreach (var key in filters.Keys) {
                //# Only add pre-system approved fields to the query
                if (!validFields.ContainsKey(key)) {
                    postQueryFilter.Add(key, filters[key]);
                    continue;
                }

                var field = validFields[key];
                var operation = GetSupportedOperation(field);
                if (operation.IsOk) {
                    /*
                    # WIQL supports wrapping values in ' or " and escaping ' by doubling it
                    #
                    # For this System.Title: hi'there
                    # use this query fragment: [System.Title] = 'hi''there'
                    #
                    # For this System.Title: hi"there
                    # use this query fragment: [System.Title] = 'hi"there'
                    #
                    # For this System.Title: hi'"there
                    # use this query fragment: [System.Title] = 'hi''"there'
                    */
                    var single = "'";
                    parts.Add($"[{key}] {operation.OkV} '{filters[key].Replace(single, single + single)}'");
                } else {
                    _logTracer.LogWarning("{error}", operation.ErrorV);
                }
            }

            var query = "select [System.Id] from WorkItems order by [System.Id]";
            if (parts != null && parts.Any()) {
                query += " where " + string.Join(" AND ", parts);
            }

            var wiql = new Wiql() {
                Query = query
            };

            foreach (var workItemReference in (await _client.QueryByWiqlAsync(wiql)).WorkItems) {
                var item = await _client.GetWorkItemAsync(_project, workItemReference.Id, expand: WorkItemExpand.All);

                var loweredFields = item.Fields.ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => JsonSerializer.Serialize(kvp.Value));
                if (postQueryFilter.Any() && !postQueryFilter.All(kvp => {
                    var lowerKey = kvp.Key.ToLowerInvariant();
                    return loweredFields.ContainsKey(lowerKey) && loweredFields[lowerKey] == postQueryFilter[kvp.Key];
                })) {
                    continue;
                }

                yield return item;
            }
        }

        /// <returns>true if the state of the item was modified</returns>
        public async Async.Task<bool> UpdateExisting(WorkItem item, IList<(string, string)> notificationInfo) {
            _logTracer.AddTags(notificationInfo);
            _logTracer.AddTag("ItemId", item.Id.HasValue ? item.Id.Value.ToString() : "");

            if (MatchesUnlessCase(item)) {
                _logTracer.LogMetric("WorkItemMatchedUnlessCase", 1);
                return false;
            }

            if (_config.OnDuplicate.Comment != null) {
                var comment = _config.OnDuplicate.Comment;
                _ = await _client.AddCommentAsync(
                    new CommentCreate() {
                        Text = comment
                    },
                    _project,
                    (int)item.Id!);
            }

            var document = new JsonPatchDocument();
            foreach (var field in _config.OnDuplicate.Increment) {
                var value = item.Fields.TryGetValue(field, out var fieldValue) ? int.Parse(JsonSerializer.Serialize(fieldValue)) : 0;
                value++;
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = $"/fields/{field}",
                    Value = value.ToString()
                });
            }

            foreach (var field in _config.OnDuplicate.AdoFields) {
                var fieldValue = _config.OnDuplicate.AdoFields[field.Key];
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = $"/fields/{field.Key}",
                    Value = fieldValue
                });
            }

            // the below was causing on_duplicate not to work
            // var systemState = JsonSerializer.Serialize(item.Fields["System.State"]);
            var systemState = (string)item.Fields["System.State"];
            var stateUpdated = false;
            if (_config.OnDuplicate.SetState.TryGetValue(systemState, out var v)) {
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/System.State",
                    Value = v
                });

                stateUpdated = true;
            }

            if (document.Any()) {
                _ = await _client.UpdateWorkItemAsync(document, _project, (int)item.Id!);
                var adoEventType = "AdoUpdate";
                _logTracer.LogEvent(adoEventType);

            } else {
                var adoEventType = "AdoNoUpdate";
                _logTracer.LogEvent(adoEventType);
            }

            return stateUpdated;
        }

        private bool MatchesUnlessCase(WorkItem workItem) =>
            _config.OnDuplicate.Unless != null &&
            _config.OnDuplicate.Unless
                // Any condition from the list may match
                .Any(condition => condition
                    // All fields within the condition must match
                    .All(kvp =>
                        workItem.Fields.TryGetValue<string>(kvp.Key, out var value) &&
                        string.Equals(kvp.Value, value, StringComparison.OrdinalIgnoreCase)));

        private async Async.Task<WorkItem> CreateNew() {
            var (taskType, document) = RenderNew();
            var entry = await _client.CreateWorkItemAsync(document, _project, taskType);

            if (_config.Comment != null) {
                var comment = _config.Comment;
                _ = await _client.AddCommentAsync(
                    new CommentCreate() {
                        Text = comment,
                    },
                    _project,
                    (int)entry.Id!);
            }
            return entry;
        }

        private (string, JsonPatchDocument) RenderNew() {
            var taskType = _config.Type;
            var document = new JsonPatchDocument();
            if (!_config.AdoFields.ContainsKey("System.Tags")) {
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = "Onefuzz"
                });
            }

            foreach (var field in _config.AdoFields.Keys) {
                var value = _config.AdoFields[field];

                if (string.Equals(field, "System.Tags")) {
                    value += ";Onefuzz";
                }

                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = $"/fields/{field}",
                    Value = value
                });
            }

            return (taskType, document);
        }

        public async Async.Task Process(IList<(string, string)> notificationInfo) {
            var updated = false;
            WorkItem? oldestWorkItem = null;
            await foreach (var workItem in ExistingWorkItems(notificationInfo)) {
                // work items are ordered by id, so the oldest one is the first one
                oldestWorkItem ??= workItem;
                using (_logTracer.BeginScope("Search matching work items")) {
                    _logTracer.AddTags(new List<(string, string)> { ("MatchingWorkItemIds", $"{workItem.Id}") });
                    _logTracer.LogInformation("Found matching work item");
                }
                if (IsADODuplicateWorkItem(workItem)) {
                    continue;
                }

                using (_logTracer.BeginScope("Non-duplicate work item")) {
                    _logTracer.AddTags(new List<(string, string)> { ("NonDuplicateWorkItemId", $"{workItem.Id}") });
                    _logTracer.LogInformation("Found matching non-duplicate work item");
                }

                _ = await UpdateExisting(workItem, notificationInfo);
                updated = true;
            }

            if (!updated) {
                if (oldestWorkItem != null) {
                    // We have matching work items but all are duplicates
                    _logTracer.AddTags(notificationInfo);
                    _logTracer.LogInformation($"All matching work items were duplicates, re-opening the oldest one");
                    var stateChanged = await UpdateExisting(oldestWorkItem, notificationInfo);
                    if (stateChanged) {
                        // add a comment if we re-opened the bug
                        _ = await _client.AddCommentAsync(
                            new CommentCreate() {
                                Text =
                                    "This work item was re-opened because OneFuzz could only find related work items that are marked as duplicate."
                            },
                            _project,
                            (int)oldestWorkItem.Id!);
                    }
                } else {
                    // We never saw a work item like this before, it must be new
                    var entry = await CreateNew();
                    var adoEventType = "AdoNewItem";
                    _logTracer.AddTags(notificationInfo);
                    _logTracer.AddTag("WorkItemId", entry.Id.HasValue ? entry.Id.Value.ToString() : "");
                    _logTracer.LogEvent(adoEventType);
                }
            }
        }

        private static bool IsADODuplicateWorkItem(WorkItem wi) {
            // A work item could have System.State == Resolve && System.Reason == Duplicate
            // OR it could have System.State == Closed && System.Reason == Duplicate
            // I haven't found any other combinations where System.Reason could be duplicate but just to be safe
            // we're explicitly _not_ checking the state of the work item to determine if it's duplicate
            return wi.Fields.ContainsKey("System.Reason") && string.Equals(wi.Fields["System.Reason"].ToString(), "Duplicate")
            // Alternatively, the work item can also specify a 'relation' to another work item.
            // This is typically used to create parent/child relationships between work items but can also
            // Be used to mark duplicates so we should check this as well.
            // ADO has 2 relation types relating to duplicates: "Duplicate" and  "Duplicate Of"
            // When work item A has a link type "Duplicate" to work item B, B automatically gains a link type "Duplicate Of" pointing to A
            // It's my understanding that the work item containing the "Duplicate" link should be the original while work items containing
            // "Duplicate Of" are the duplicates. That is why we search for the relation type "Duplicate Of".
            // "Duplicate Of" has the relation type: "System.LinkTypes.Duplicate-Forward"
            // Source: https://learn.microsoft.com/en-us/azure/devops/boards/queries/link-type-reference?view=azure-devops#work-link-types
            || wi.Relations != null && wi.Relations.Any(relation => string.Equals(relation.Rel, "System.LinkTypes.Duplicate-Forward"));
        }

        private static OneFuzzResult<string> GetSupportedOperation(WorkItemField field) {
            return field.SupportedOperations switch {
                var supportedOps when supportedOps.Any(op => op.ReferenceName == "SupportedOperations.Equals") => OneFuzzResult.Ok("="),
                var supportedOps when supportedOps.Any(op => op.ReferenceName == "SupportedOperations.ContainsWords") => OneFuzzResult.Ok("Contains Words"),
                _ => OneFuzzResult<string>.Error(ErrorCode.UNSUPPORTED_FIELD_OPERATION, $"OneFuzz only support operations ['Equals', 'ContainsWords']. Field {field.ReferenceName} only support operations: {string.Join(',', field.SupportedOperations.Select(op => op.ReferenceName))}"),
            };
        }
    }
}
