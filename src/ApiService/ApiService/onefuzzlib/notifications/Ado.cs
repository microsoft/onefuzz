using System.Text.Json;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace Microsoft.OneFuzz.Service;

public interface IAdo {
    public Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport reportable, bool failTaskonTransientError);

}

public class Ado : NotificationsBase, IAdo {
    public Ado(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport reportable, bool failTaskonTransientError) {
        if (reportable is RegressionReport) {
            _logTracer.Info($"ado integration does not support regressiong report. container:{container} filename:{filename}");
            return;
        }

        var report = (Report)reportable;

        var notificationInfo = @$"job_id:{report.JobId} task_id:{report.TaskId}
         container:{container} filename:{filename}";

        _logTracer.Info($"notify ado: {notificationInfo}");

        try {
            var ado = await AdoConnector.AdoConnectorCreator(_context, container, filename, config, report);
            await ado.Process(notificationInfo);
        } catch (Exception e) {
            /*
            TODO: Catch these
                AzureDevOpsAuthenticationError,
                AzureDevOpsClientError,
                AzureDevOpsServiceError,
                AzureDevOpsClientRequestError,
                ValueError,
            */
            if (!failTaskonTransientError && IsTransient(e)) {
                // In the python code we rethrow the exception but we'll lose the stack info
                // Instead, I'm logging an error here that it's a transient ADO failure
                _logTracer.Error($"transient ADO notification failure {notificationInfo}");
                throw;
            } else {
                await FailTask(report, e);
            }
        }
    }

    private static bool IsTransient(Exception e) {
        var errorCodes = new List<string>()
        {
            //# "TF401349: An unexpected error has occurred, please verify your request and try again." # noqa: E501
            "TF401349",
            //# TF26071: This work item has been changed by someone else since you opened it. You will need to refresh it and discard your changes. # noqa: E501
            "TF26071",
        };

        var errorStr = e.ToString();
        return errorCodes.Any(code => errorStr.Contains(code));
    }

    class AdoConnector {
        private readonly AdoTemplate _config;
        private readonly Renderer _renderer;
        private readonly string _project;
        private readonly WorkItemTrackingHttpClient _client;
        private Uri _instanceUrl;
        public static async Async.Task<AdoConnector> AdoConnectorCreator(IOnefuzzContext context, Container container, string filename, AdoTemplate config, Report report, Renderer? renderer = null) {
            renderer ??= await Renderer.ConstructRenderer(context, container, filename, report);
            var instanceUrl = context.Creds.GetInstanceUrl();
            var project = await renderer.Render(config.Project, instanceUrl);

            var authToken = await context.SecretsOperations.GetSecretStringValue(config.AuthToken);
            var client = GetAdoClient(config.BaseUrl, authToken!);
            return new AdoConnector(container, filename, config, report, renderer, project!, client, instanceUrl);
        }

        private static WorkItemTrackingHttpClient GetAdoClient(Uri baseUrl, string token) {
            return new WorkItemTrackingHttpClient(baseUrl, new VssBasicCredential("PAT", token));
        }
        public AdoConnector(Container container, string filename, AdoTemplate config, Report report, Renderer renderer, string project, WorkItemTrackingHttpClient client, Uri instanceUrl) {
            _config = config;
            _renderer = renderer;
            _project = project;
            _client = client;
            _instanceUrl = instanceUrl;
        }

        public async Async.Task<string> Render(string template) {
            return await _renderer.Render(template, _instanceUrl);
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

            var validFields = await GetValidFields(filters["system.teamproject"]);

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
                if (!validFields.Contains(key)) {
                    postQueryFilter.Add(key, filters[key]);
                    continue;
                }

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
                parts.Add($"[{key}] = '{filters[key].Replace(single, single + single)}'");
            }

            var query = "select [System.Id] from WorkItems";
            if (parts != null && parts.Any()) {
                query += " where " + string.Join(" AND ", parts);
            }

            var wiql = new Wiql() {
                Query = query
            };

            foreach (var workItemReference in (await _client.QueryByWiqlAsync(wiql)).WorkItems) {
                var item = await _client.GetWorkItemAsync(_project, workItemReference.Id, expand: WorkItemExpand.Fields);

                // This code is questionable
                var loweredFields = item.Fields.ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => JsonSerializer.Serialize(kvp.Value));
                if (postQueryFilter.Any() && !postQueryFilter.All(kvp => {
                    var lowerKey = kvp.Key.ToLowerInvariant();
                    return loweredFields.ContainsKey(lowerKey) && loweredFields[lowerKey] == postQueryFilter[kvp.Key];
                })) {
                    continue;
                }
                // End questionable code

                yield return item;
            }
        }

        public async Async.Task UpdateExisting(WorkItem item, string notificationInfo) {
            if (_config.OnDuplicate.Comment != null) {
                var comment = await Render(_config.OnDuplicate.Comment);
                await _client.AddCommentAsync(
                    new CommentCreate() {
                        Text = comment
                    },
                    _project,
                    (int)(item.Id!)
                );
            }

            var document = new JsonPatchDocument();
            foreach (var field in _config.OnDuplicate.Increment) {
                var value = item.Fields.ContainsKey(field) ? int.Parse(JsonSerializer.Serialize(item.Fields[field])) : 0;
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
                    Path = $"/fields/{field}",
                    Value = fieldValue
                });
            }

            var systemState = JsonSerializer.Serialize(item.Fields["System.State"]);
            if (_config.OnDuplicate.SetState.ContainsKey(systemState)) {
                document.Add(new JsonPatchOperation() {
                    Operation = VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/System.State",
                    Value = _config.OnDuplicate.SetState[systemState]
                });
            }

            if (document.Any()) {
                await _client.UpdateWorkItemAsync(document, _project, (int)(item.Id!));
                // TODO: _logging.Info($""notify ado: updated work item {item.Id} - {notificationInfo}); 
            } else {
                // TODO: _logging.Info($"notify ado: no update for work item {item.Id} - {notificationInfo}");
            }
        }

        private async Async.Task<List<string>> GetValidFields(string? project) {
            return (await _client.GetFieldsAsync(project, expand: GetFieldsExpand.ExtensionFields))
                .Select(field => field.ReferenceName.ToLowerInvariant())
                .ToList();
        }

        private async Async.Task<WorkItem> CreateNew() {
            var (taskType, document) = await RenderNew();
            var entry = await _client.CreateWorkItemAsync(document, _project, taskType);

            if (_config.Comment != null) {
                var comment = await Render(_config.Comment);
                await _client.AddCommentAsync(
                    new CommentCreate() {
                        Text = comment,
                    },
                    _project,
                    (int)(entry.Id!)
                );
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

        public async Async.Task Process(string notificationInfo) {
            var seen = false;
            await foreach (var workItem in ExistingWorkItems()) {
                await UpdateExisting(workItem, notificationInfo);
                seen = true;
            }

            if (!seen) {
                var entry = await CreateNew();
                // TODO: _logTracer.Info($"notify ado: created new work item {entry.Id} - {notificationInfo}");
            }
        }
    }
}
