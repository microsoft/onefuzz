using System.Net.Http;
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
    // https://github.com/MicrosoftDocs/azure-devops-docs/issues/5890#issuecomment-539632059
    private const int MAX_SYSTEM_TITLE_LENGTH = 128;
    private const string TITLE_FIELD = "System.Title";
    private static List<string> DEFAULT_REGRESSION_IGNORE_STATES = new() { "New", "Commited", "Active" };

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
            await ProcessNotification(_context, container, filename, config, report, _logTracer, notificationInfo, isRegression: reportable is RegressionReport);
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
        return errorCodes.Any(errorStr.Contains);
    }

    public static OneFuzzResultVoid ValidateTreePath(IEnumerable<string> path, WorkItemClassificationNode? root) {
        if (root is null) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PROJECT, new string[] {
                $"Path \"{string.Join('\\', path)}\" is invalid. The specified ADO project doesn't exist.",
                "Double check the 'project' field in your ADO config.",
            });
        }

        string treeNodeTypeName;
        switch (root.StructureType) {
            case TreeNodeStructureType.Area:
                treeNodeTypeName = "Area";
                break;
            case TreeNodeStructureType.Iteration:
                treeNodeTypeName = "Iteration";
                break;
            default:
                return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                    $"Path root \"{root.Name}\" is an unsupported type. Expected Area or Iteration but got {root.StructureType}.",
                });
        }

        // Validate path based on
        // https://learn.microsoft.com/en-us/azure/devops/organizations/settings/about-areas-iterations?view=azure-devops#naming-restrictions
        var maxNodeLength = 255;
        var maxDepth = 13;
        // Invalid characters from the link above plus the escape sequences (since they have backslashes and produce confusingly formatted errors if not caught here)
        var invalidChars = new char[] { '/', ':', '*', '?', '"', '<', '>', '|', ';', '#', '$', '*', '{', '}', ',', '+', '=', '[', ']' };

        // Ensure that none of the path parts are too long
        var erroneous = path.FirstOrDefault(part => part.Length > maxNodeLength);
        if (erroneous != null) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. \"{erroneous}\" is too long. It must be less than {maxNodeLength} characters.",
                "Learn more about naming restrictions here: https://learn.microsoft.com/en-us/azure/devops/organizations/settings/about-areas-iterations?view=azure-devops#naming-restrictions"
            });
        }

        // Ensure that none of the path parts contain invalid characters
        erroneous = path.FirstOrDefault(part => invalidChars.Any(part.Contains));
        if (erroneous != null) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. \"{erroneous}\" contains an invalid character ({string.Join(" ", invalidChars)}).",
                "Make sure that the path is separated by backslashes (\\) and not forward slashes (/).",
                "Learn more about naming restrictions here: https://learn.microsoft.com/en-us/azure/devops/organizations/settings/about-areas-iterations?view=azure-devops#naming-restrictions"
            });
        }

        // Ensure no unicode control characters
        erroneous = path.FirstOrDefault(part => part.Any(ch => char.IsControl(ch)));
        if (erroneous != null) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                // More about control codes and their range here: https://en.wikipedia.org/wiki/Unicode_control_characters
                $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. \"{erroneous}\" contains a unicode control character (\\u0000 - \\u001F or \\u007F - \\u009F).",
                "Make sure that you're path doesn't contain any escape characters (\\0 \\a \\b \\f \\n \\r \\t \\v).",
                "Learn more about naming restrictions here: https://learn.microsoft.com/en-us/azure/devops/organizations/settings/about-areas-iterations?view=azure-devops#naming-restrictions"
            });
        }

        // Ensure that there aren't too many path parts
        if (path.Count() > maxDepth) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. It must be less than {maxDepth} levels deep.",
                "Learn more about naming restrictions here: https://learn.microsoft.com/en-us/azure/devops/organizations/settings/about-areas-iterations?view=azure-devops#naming-restrictions"
            });
        }


        // Path should always start with the project name ADO expects an absolute path
        if (!string.Equals(path.First(), root.Name, StringComparison.OrdinalIgnoreCase)) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. It must start with the project name, \"{root.Name}\".",
                $"Example: \"{root.Name}\\{path}\".",
            });
        }

        // Validate that each part of the path is a valid child of the previous part
        var current = root;
        foreach (var part in path.Skip(1)) {
            var child = current.Children?.FirstOrDefault(x => string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
            if (child == null) {
                if (current.Children is null || !current.Children.Any()) {
                    return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                        $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. \"{current.Name}\" has no children.",
                    });
                } else {
                    return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_INVALID_PATH, new string[] {
                        $"{treeNodeTypeName} Path \"{string.Join('\\', path)}\" is invalid. \"{part}\" is not a valid child of \"{current.Name}\".",
                        $"Valid children of \"{current.Name}\" are: [{string.Join(',', current.Children?.Select(x => $"\"{x.Name}\"") ?? new List<string>())}].",
                    });
                }
            }

            current = child;
        }

        return OneFuzzResultVoid.Ok;
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

        var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>();
        try {
            // Validate unique_fields are part of the project's valid fields
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

        try {
            // Validate AreaPath and IterationPath exist
            // This also validates that the config.Project exists
            if (config.AdoFields.TryGetValue("System.AreaPath", out var areaPathString)) {
                var path = areaPathString.Split('\\');
                var root = await witClient.GetClassificationNodeAsync(config.Project, TreeStructureGroup.Areas, depth: path.Length - 1);
                var validateAreaPath = ValidateTreePath(path, root);
                if (!validateAreaPath.IsOk) {
                    return validateAreaPath;
                }
            }
            if (config.AdoFields.TryGetValue("System.IterationPath", out var iterationPathString)) {
                var path = iterationPathString.Split('\\');
                var root = await witClient.GetClassificationNodeAsync(config.Project, TreeStructureGroup.Iterations, depth: path.Length - 1);
                var validateIterationPath = ValidateTreePath(path, root);
                if (!validateIterationPath.IsOk) {
                    return validateIterationPath;
                }
            }
        } catch (Exception e) {
            return OneFuzzResultVoid.Error(ErrorCode.ADO_VALIDATION_UNEXPECTED_ERROR, new string[] {
                "Failed to query and validate against the classification nodes for this project",
                $"Exception: {e}",
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

    private static async Async.Task ProcessNotification(IOnefuzzContext context, Container container, string filename, AdoTemplate config, Report report, ILogger logTracer, IList<(string, string)> notificationInfo, Renderer? renderer = null, bool isRegression = false) {
        if (!config.AdoFields.TryGetValue(TITLE_FIELD, out var issueTitle)) {
            issueTitle = "{{ report.crash_site }} - {{ report.executable }}";
        }
        var instanceUrl = context.Creds.GetInstanceUrl();
        renderer ??= await Renderer.ConstructRenderer(context, container, filename, issueTitle, report, instanceUrl, logTracer);
        var project = renderer.Render(config.Project, instanceUrl);

        var authToken = await context.SecretsOperations.GetSecretValue(config.AuthToken.Secret);
        var client = GetAdoClient(config.BaseUrl, authToken!);

        var renderedConfig = RenderAdoTemplate(logTracer, renderer, config, instanceUrl);
        var ado = new AdoConnector(renderedConfig, project!, client, instanceUrl, logTracer, await GetValidFields(client, project));
        await ado.Process(notificationInfo, isRegression);
    }

    public static RenderedAdoTemplate RenderAdoTemplate(ILogger logTracer, Renderer renderer, AdoTemplate original, Uri instanceUrl) {
        var adoFields = original.AdoFields.ToDictionary(kvp => kvp.Key, kvp => Render(renderer, kvp.Value, instanceUrl, logTracer));
        var onDuplicateAdoFields = original.OnDuplicate.AdoFields.ToDictionary(kvp => kvp.Key, kvp => Render(renderer, kvp.Value, instanceUrl, logTracer));

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
                dict.ToDictionary(kvp => kvp.Key, kvp => Render(renderer, kvp.Value, instanceUrl, logTracer)))
                .ToList();

        var onDuplicate = new ADODuplicateTemplate(
            original.OnDuplicate.Increment,
            original.OnDuplicate.SetState,
            onDuplicateAdoFields,
            original.OnDuplicate.Comment != null ? Render(renderer, original.OnDuplicate.Comment, instanceUrl, logTracer) : null,
            onDuplicateUnless,
            original.OnDuplicate.RegressionIgnoreStates
        );

        return new RenderedAdoTemplate(
            original.BaseUrl,
            original.AuthToken,
            Render(renderer, original.Project, instanceUrl, logTracer),
            Render(renderer, original.Type, instanceUrl, logTracer),
            original.UniqueFields,
            adoFields,
            onDuplicate,
            original.AdoDuplicateFields,
            original.Comment != null ? Render(renderer, original.Comment, instanceUrl, logTracer) : null
        );
    }

    private static string Render(Renderer renderer, string toRender, Uri instanceUrl, ILogger logTracer) {
        try {
            return renderer.Render(toRender, instanceUrl, strictRendering: true);
        } catch {
            logTracer.LogWarning("Failed to render template in strict mode. Falling back to relaxed mode. {Template} ", toRender);
            return renderer.Render(toRender, instanceUrl, strictRendering: false);
        }
    }

    public sealed class AdoConnector {
        private readonly RenderedAdoTemplate _config;
        private readonly string _project;
        private readonly WorkItemTrackingHttpClient _client;
        private readonly ILogger _logTracer;
        private readonly Dictionary<string, WorkItemField2> _validFields;

        public AdoConnector(RenderedAdoTemplate config, string project, WorkItemTrackingHttpClient client, Uri instanceUrl, ILogger logTracer, Dictionary<string, WorkItemField2> validFields) {
            _config = config;
            _project = project;
            _client = client;
            _logTracer = logTracer;
            _validFields = validFields;
        }

        public async IAsyncEnumerable<WorkItem> ExistingWorkItems(IList<(string, string)> notificationInfo) {
            var (wiql, postQueryFilter) = CreateExistingWorkItemsQuery(notificationInfo);
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

        public (Wiql, Dictionary<string, string>) CreateExistingWorkItemsQuery(IList<(string, string)> notificationInfo) {
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
                if (!_validFields.ContainsKey(key)) {
                    postQueryFilter.Add(key, filters[key]);
                    continue;
                }

                var field = _validFields[key];
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

            _logTracer.LogInformation("{Query}", query);
            return (new Wiql() {
                Query = query
            }, postQueryFilter);
        }

        /// <returns>true if the state of the item was modified</returns>
        public async Async.Task<bool> UpdateExisting(WorkItem item, IList<(string, string)> notificationInfo) {
            _logTracer.AddTags(notificationInfo);
            _logTracer.AddTag("ItemId", item.Id.HasValue ? item.Id.Value.ToString() : "");

            if (MatchesUnlessCase(item)) {
                _logTracer.LogMetric("WorkItemMatchedUnlessCase", 1);
                return false;
            }

            if (!string.IsNullOrEmpty(_config.OnDuplicate.Comment)) {
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

        public async Async.Task Process(IList<(string, string)> notificationInfo, bool isRegression) {
            var updated = false;
            WorkItem? oldestWorkItem = null;
            await foreach (var workItem in ExistingWorkItems(notificationInfo)) {
                // work items are ordered by id, so the oldest one is the first one
                oldestWorkItem ??= workItem;
                using (_logTracer.BeginScope("Search matching work items")) {
                    _logTracer.AddTags(new List<(string, string)> { ("MatchingWorkItemIds", $"{workItem.Id}") });
                    _logTracer.LogInformation("Found matching work item");
                }
                if (IsADODuplicateWorkItem(workItem, _config.AdoDuplicateFields)) {
                    continue;
                }

                var regressionStatesToIgnore = _config.OnDuplicate.RegressionIgnoreStates != null ? _config.OnDuplicate.RegressionIgnoreStates : DEFAULT_REGRESSION_IGNORE_STATES;
                if (isRegression) {
                    var state = (string)workItem.Fields["System.State"];
                    if (regressionStatesToIgnore.Contains(state, StringComparer.InvariantCultureIgnoreCase))
                        continue;
                }

                using (_logTracer.BeginScope("Non-duplicate work item")) {
                    _logTracer.AddTags(new List<(string, string)> { ("NonDuplicateWorkItemId", $"{workItem.Id}") });
                    _logTracer.LogInformation("Found matching non-duplicate work item");
                }

                _ = await UpdateExisting(workItem, notificationInfo);
                updated = true;
            }

            if (updated || isRegression) {
                return;
            }

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

        private static bool IsADODuplicateWorkItem(WorkItem wi, Dictionary<string, string>? duplicateFields) {
            // A work item could have System.State == Resolve && System.Reason == Duplicate
            // OR it could have System.State == Closed && System.Reason == Duplicate
            // I haven't found any other combinations where System.Reason could be duplicate but just to be safe
            // we're explicitly _not_ checking the state of the work item to determine if it's duplicate
            return wi.Fields.ContainsKey("System.Reason") && string.Equals(wi.Fields["System.Reason"].ToString(), "Duplicate", StringComparison.OrdinalIgnoreCase)
            || wi.Fields.ContainsKey("Microsoft.VSTS.Common.ResolvedReason") && string.Equals(wi.Fields["Microsoft.VSTS.Common.ResolvedReason"].ToString(), "Duplicate", StringComparison.OrdinalIgnoreCase)
            || duplicateFields?.Any(fieldPair => {
                var (field, value) = fieldPair;
                return wi.Fields.ContainsKey(field) && string.Equals(wi.Fields[field].ToString(), value, StringComparison.OrdinalIgnoreCase);
            }) == true
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
