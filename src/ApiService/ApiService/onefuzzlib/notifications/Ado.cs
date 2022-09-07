using System.Text.Json;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;

namespace Microsoft.OneFuzz.Service;

public interface IAdo {
    public Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport report, bool failTaskonTransientError);

}

public class Ado : NotificationsBase, IAdo {
    public Ado(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task NotifyAdo(AdoTemplate config, Container container, string filename, IReport report, bool failTaskonTransientError) {
        if (report is RegressionReport) {
            _logTracer.Info($"ado integration does not support regressiong report. container:{container} filename:{filename}");
            return;
        }
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
            foreach(var key in _config.UniqueFields) {
                var filter = string.Empty;
                if (string.Equals("System.TeamProject", key)) {
                    filter = await Render(_config.Project);
                }
                else {
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
            foreach(var key in filters.Keys) {
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

            var wiql = new Wiql()
            {
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
            
        }

        private async Async.Task<List<string>> GetValidFields(string? project) {
            return (await _client.GetFieldsAsync(project, expand: GetFieldsExpand.ExtensionFields))
                .Select(field => field.ReferenceName.ToLowerInvariant())
                .ToList();
        }
    }
}
