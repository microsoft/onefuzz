using System.Text.Json;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace Microsoft.OneFuzz.Service;

public interface IAdo {
    public Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport reportable, bool isLastRetryAttempt, Guid notificationId);
}

public class Ado : NotificationsBase, IAdo {
    public Ado(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport reportable, bool isLastRetryAttempt, Guid notificationId) {
        if (reportable is RegressionReport) {
            _logTracer.Info($"ado integration does not support regression report. container:{container:Tag:Container} filename:{filename:Tag:Filename}");
            return;
        }

        var report = (Report)reportable;

        (string, string)[] notificationInfo = { ("notification_id", notificationId.ToString()), ("job_id", report.JobId.ToString()), ("task_id", report.TaskId.ToString()), ("ado_project", config.Project), ("ado_url", config.BaseUrl.ToString()), ("container", container.String), ("filename", filename) };

        var adoEventType = "AdoNotify";
        _logTracer.WithTags(notificationInfo).Event($"{adoEventType}");

        try {
            var ado = await AdoConnector.AdoConnectorCreator(_context, container, filename, config, report, _logTracer);
            await ado.Process(notificationInfo);
        } catch (Exception e)
              when (e is VssUnauthorizedException || e is VssAuthenticationException || e is VssServiceException) {
            var _ = config.AdoFields.TryGetValue("System.AssignedTo", out var assignedTo);
            if ((e is VssAuthenticationException || e is VssUnauthorizedException) && !string.IsNullOrEmpty(assignedTo)) {
                notificationInfo = notificationInfo.AddRange(new (string, string)[] { ("assigned_to", assignedTo) });
            }

            if (!isLastRetryAttempt && IsTransient(e)) {
                _logTracer.WithTags(notificationInfo).Error($"transient ADO notification failure {report.JobId:Tag:JobId} {report.TaskId:Tag:TaskId} {container:Tag:Container} {filename:Tag:Filename}");
                throw;
            } else {
                _logTracer.WithTags(notificationInfo).Exception(e, $"Failed to process ado notification");
                await LogFailedNotification(report, e, notificationId);
            }
        }
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
            try {
                connection = new VssConnection(config.BaseUrl, new VssBasicCredential(string.Empty, token.Value));
                await connection.ConnectAsync();
            } catch {
                return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, $"Failed to connect to {config.BaseUrl} using the provided token");
            }
        } else {
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, "Auth token is missing or invalid");
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
                return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, new[]
                    {
                        $"The following unique fields are not valid fields for this project: {string.Join(',', invalidFields)}",
                        "You can find the valid fields for your project by following these steps: https://learn.microsoft.com/en-us/azure/devops/boards/work-items/work-item-fields?view=azure-devops#review-fields"
                    }
                );
            }
        } catch {
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, "Failed to query and compare the valid fields for this project");
        }

        return OneFuzzResultVoid.Ok;
    }

    private static WorkItemTrackingHttpClient GetAdoClient(Uri baseUrl, string token) {
        return new WorkItemTrackingHttpClient(baseUrl, new VssBasicCredential("PAT", token));
    }

    private static async Async.Task<Dictionary<string, WorkItemField>> GetValidFields(WorkItemTrackingHttpClient client, string? project) {
        return (await client.GetFieldsAsync(project, expand: GetFieldsExpand.ExtensionFields))
            .ToDictionary(field => field.ReferenceName.ToLowerInvariant());
    }

    sealed class AdoConnector {
        private readonly AdoTemplate _config;
        private readonly Renderer _renderer;
        private readonly string _project;
        private readonly WorkItemTrackingHttpClient _client;
        private readonly Uri _instanceUrl;
        private readonly ILogTracer _logTracer;
        public static async Async.Task<AdoConnector> AdoConnectorCreator(IOnefuzzContext context, Container container, string filename, AdoTemplate config, Report report, ILogTracer logTracer, Renderer? renderer = null) {
            renderer ??= await Renderer.ConstructRenderer(context, container, filename, report, logTracer);
            var instanceUrl = context.Creds.GetInstanceUrl();
            var project = await renderer.Render(config.Project, instanceUrl);

            var authToken = await context.SecretsOperations.GetSecretStringValue(config.AuthToken);
            var client = GetAdoClient(config.BaseUrl, authToken!);
            return new AdoConnector(config, renderer, project!, client, instanceUrl, logTracer);
        }


        public AdoConnector(AdoTemplate config, Renderer renderer, string project, WorkItemTrackingHttpClient client, Uri instanceUrl, ILogTracer logTracer) {
            _config = config;
            _renderer = renderer;
            _project = project;
            _client = client;
            _instanceUrl = instanceUrl;
            _logTracer = logTracer;
        }

        public async Async.Task<string> Render(string template) {
            try {
                return await _renderer.Render(template, _instanceUrl, strictRendering: true);
            } catch {
                _logTracer.Warning($"Failed to render template in strict mode. Falling back to relaxed mode. {template:Template}");
                return await _renderer.Render(template, _instanceUrl, strictRendering: false);
            }
        }

        public async IAsyncEnumerable<WorkItem> ExistingWorkItems() {
            var filters = new Dictionary<string, string>();
            foreach (var key in _config.UniqueFields) {
                var filter = string.Empty;
                if (string.Equals("System.TeamProject", key)) {
                    filter = await Render(_config.Project);
                } else {
                    filter = await Render(_config.AdoFields[key]);
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
                    _logTracer.Warning(operation.ErrorV);
                }
            }

            var query = "select [System.Id] from WorkItems";
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
        public async Async.Task<bool> UpdateExisting(WorkItem item, (string, string)[] notificationInfo) {
            if (_config.OnDuplicate.Comment != null) {
                var comment = await Render(_config.OnDuplicate.Comment);
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
                var fieldValue = await Render(_config.OnDuplicate.AdoFields[field.Key]);
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = $"/fields/{field.Key}",
                    Value = fieldValue
                });
            }

            var systemState = JsonSerializer.Serialize(item.Fields["System.State"]);
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
                _logTracer.WithTags(notificationInfo).Event($"{adoEventType} {item.Id:Tag:WorkItemId}");

            } else {
                var adoEventType = "AdoNoUpdate";
                _logTracer.WithTags(notificationInfo).Event($"{adoEventType} {item.Id:Tag:WorkItemId}");
            }

            return stateUpdated;
        }

        private async Async.Task<WorkItem> CreateNew() {
            var (taskType, document) = await RenderNew();
            var entry = await _client.CreateWorkItemAsync(document, _project, taskType);

            if (_config.Comment != null) {
                var comment = await Render(_config.Comment);
                _ = await _client.AddCommentAsync(
                    new CommentCreate() {
                        Text = comment,
                    },
                    _project,
                    (int)entry.Id!);
            }
            return entry;
        }

        private async Async.Task<(string, JsonPatchDocument)> RenderNew() {
            var taskType = await Render(_config.Type);
            var document = new JsonPatchDocument();
            if (!_config.AdoFields.ContainsKey("System.Tags")) {
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = "Onefuzz"
                });
            }

            foreach (var field in _config.AdoFields.Keys) {
                var value = await Render(_config.AdoFields[field]);

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

        public async Async.Task Process((string, string)[] notificationInfo) {
            var matchingWorkItems = await ExistingWorkItems().ToListAsync();

            var nonDuplicateWorkItems = matchingWorkItems
                .Where(wi => !IsADODuplicateWorkItem(wi))
                .ToList();

            if (nonDuplicateWorkItems.Count > 1) {
                var nonDuplicateWorkItemIds = nonDuplicateWorkItems.Select(wi => wi.Id);
                var matchingWorkItemIds = matchingWorkItems.Select(wi => wi.Id);

                var extraTags = new List<(string, string)> {
                    ("NonDuplicateWorkItemIds", JsonSerializer.Serialize(nonDuplicateWorkItemIds)),
                    ("MatchingWorkItemIds", JsonSerializer.Serialize(matchingWorkItemIds))
                };
                extraTags.AddRange(notificationInfo);

                _logTracer.WithTags(extraTags).Info($"Found more than 1 matching, non-duplicate work item");
                foreach (var workItem in nonDuplicateWorkItems) {
                    _ = await UpdateExisting(workItem, notificationInfo);
                }
            } else if (nonDuplicateWorkItems.Count == 1) {
                _ = await UpdateExisting(nonDuplicateWorkItems.Single(), notificationInfo);
            } else if (matchingWorkItems.Any()) {
                // We have matching work items but all are duplicates
                _logTracer.WithTags(notificationInfo).Info($"All matching work items were duplicates, re-opening the oldest one");
                var oldestWorkItem = matchingWorkItems.OrderBy(wi => wi.Id).First();
                var stateChanged = await UpdateExisting(oldestWorkItem, notificationInfo);
                if (stateChanged) {
                    // add a comment if we re-opened the bug
                    _ = await _client.AddCommentAsync(
                        new CommentCreate() {
                            Text = "This work item was re-opened because OneFuzz could only find related work items that are marked as duplicate."
                        },
                        _project,
                        (int)oldestWorkItem.Id!);
                }
            } else {
                // We never saw a work item like this before, it must be new
                var entry = await CreateNew();
                var adoEventType = "AdoNewItem";
                _logTracer.WithTags(notificationInfo).Event($"{adoEventType} {entry.Id:Tag:WorkItemId}");
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
