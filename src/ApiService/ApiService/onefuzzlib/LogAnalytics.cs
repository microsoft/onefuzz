using Azure.Core;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.OperationalInsights;

namespace Microsoft.OneFuzz.Service;

public record MonitorSettings(string Id, string Key);

public interface ILogAnalytics {
    public ResourceIdentifier GetWorkspaceId();
    public Async.Task<MonitorSettings> GetMonitorSettings();
    public MonitorManagementClient GetMonitorManagementClient();
}


public class LogAnalytics : ILogAnalytics {

    ICreds _creds;
    IServiceConfig _config;

    public LogAnalytics(ICreds creds, IServiceConfig config) {
        _creds = creds;
        _config = config;
    }

    private AccessToken GetToken() {
        string[] scopes = { "https://management.azure.com/.default" };
        return _creds.GetIdentity().GetToken(new TokenRequestContext(scopes));
    }


    public async Async.Task<MonitorSettings> GetMonitorSettings() {
        var token = GetToken();
        var client = new OperationalInsightsManagementClient(new Rest.TokenCredentials(token.Token)) { SubscriptionId = _creds.GetSubscription() };

        var customerId = (await client.Workspaces.ListByResourceGroupAsync(_creds.GetBaseResourceGroup()))
                        .Select(w => w.CustomerId)
                        .First();
        var keys = await client.SharedKeys.GetSharedKeysAsync(_creds.GetBaseResourceGroup(), _config.OneFuzzMonitor);
        return new MonitorSettings(customerId, keys.PrimarySharedKey);
    }


    public ResourceIdentifier GetWorkspaceId() {
        return new ResourceIdentifier($"/subscriptions/{_creds.GetSubscription()}/resourceGroups/{_creds.GetBaseResourceGroup()}/providers/microsoft.operationalinsights/workspaces/{_config.OneFuzzInstanceName}");
    }

    public MonitorManagementClient GetMonitorManagementClient() {
        var token = GetToken();
        return new MonitorManagementClient(new Rest.TokenCredentials(token.Token)) { SubscriptionId = _creds.GetSubscription() };
    }

}
