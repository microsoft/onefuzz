using ApiService.OneFuzzLib.Orm;
using Microsoft.Azure.Management.Monitor;

namespace Microsoft.OneFuzz.Service;

public interface IAutoScaleOperations {
    Async.Task<AutoScale> Create(
        Guid scalesetId,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        int scaleOutAmount,
        int scaleOutCooldown,
        int scaleInAmount,
        int scaleInCooldown);

    Async.Task<AutoScale?> GetSettingsForScaleset(Guid scalesetId);


    Azure.Management.Monitor.Models.AutoscaleProfile CreateAutoScaleProfile(
        string queueUri,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        int scaleOutAmount,
        double scaleOutCooldownMinutes,
        int scaleInAmount,
        double scaleInCooldownMinutes);

    Azure.Management.Monitor.Models.AutoscaleProfile DeafaultAutoScaleProfile(string queueUri, long scaleSetSize);
    Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, Azure.Management.Monitor.Models.AutoscaleProfile autoScaleProfile);
}


public class AutoScaleOperations : Orm<AutoScale>, IAutoScaleOperations {

    public AutoScaleOperations(ILogTracer log, IOnefuzzContext context)
    : base(log, context) {

    }

    public async Async.Task<AutoScale> Create(
    Guid scalesetId,
    long minAmount,
    long maxAmount,
    long defaultAmount,
    int scaleOutAmount,
    int scaleOutCooldown,
    int scaleInAmount,
    int scaleInCooldown) {

        var entry = new AutoScale(
                scalesetId,
                Min: minAmount,
                Max: maxAmount,
                Default: defaultAmount,
                ScaleOutAmount: scaleOutAmount,
                ScaleOutCoolDown: scaleOutCooldown,
                ScaleInAmount: scaleInAmount,
                ScaleInCoolDown: scaleInCooldown
                );

        var r = await Insert(entry);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to save auto-scale record for scaleset ID: {scalesetId}, minAmount: {minAmount}, maxAmount: {maxAmount}, defaultAmount: {defaultAmount}, scaleOutAmount: {scaleOutAmount}, scaleOutCooldown: {scaleOutCooldown}, scaleInAmount: {scaleInAmount}, scaleInCooldown: {scaleInCooldown}");
        }
        return entry;
    }

    public async Async.Task<AutoScale?> GetSettingsForScaleset(Guid scalesetId) {
        var autoscale = await GetEntityAsync(scalesetId.ToString(), scalesetId.ToString());
        return autoscale;
    }


    public async Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, Azure.Management.Monitor.Models.AutoscaleProfile autoScaleProfile) {
        _logTracer.Info($"Checking scaleset {vmss} for existing auto scale resource");
        var monitorManagementClient = _context.LogAnalytics.GetMonitorManagementClient();
        var resourceGroup = _context.Creds.GetBaseResourceGroup();

        try {
            var autoScaleCollections = await monitorManagementClient.AutoscaleSettings.ListByResourceGroupAsync(resourceGroup);
            do {
                foreach (var autoScale in autoScaleCollections) {
                    if (autoScale.TargetResourceUri.EndsWith(vmss.ToString())) {
                        _logTracer.Warning($"scaleset {vmss} already has an autoscale resource");
                        return OneFuzzResultVoid.Ok;
                    }
                }
                autoScaleCollections = await monitorManagementClient.AutoscaleSettings.ListByResourceGroupNextAsync(autoScaleCollections.NextPageLink);
            } while (autoScaleCollections is not null);

        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, $"Failed to check if scaleset {vmss} already has an autoscale resource");
        }

        var autoScaleResource = await CreateAutoScaleResourceFor(vmss, await _context.Creds.GetBaseRegion(), autoScaleProfile);
        if (!autoScaleResource.IsOk) {
            return OneFuzzResultVoid.Error(autoScaleResource.ErrorV);
        }

        var diagnosticsResource = await SetupAutoScaleDiagnostics(autoScaleResource.OkV.Id, autoScaleResource.OkV.Name, _context.LogAnalytics.GetWorkspaceId().ToString());
        if (!diagnosticsResource.IsOk) {
            return OneFuzzResultVoid.Error(diagnosticsResource.ErrorV);
        }

        return OneFuzzResultVoid.Ok;
    }
    private async Async.Task<OneFuzzResult<Azure.Management.Monitor.Models.AutoscaleSettingResource>> CreateAutoScaleResourceFor(Guid resourceId, string location, Azure.Management.Monitor.Models.AutoscaleProfile profile) {
        _logTracer.Info($"Creating auto-scale resource for: {resourceId}");
        var monitorManagementClient = _context.LogAnalytics.GetMonitorManagementClient();

        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        var subscription = _context.Creds.GetSubscription();

        var scalesetUri = $"/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{resourceId}";
        var parameters = new Azure.Management.Monitor.Models.AutoscaleSettingResource();
        parameters.Location = location;
        parameters.Profiles.Add(profile);
        parameters.TargetResourceUri = scalesetUri;
        parameters.Enabled = true;

        try {
            var autoScaleResource = await monitorManagementClient.AutoscaleSettings.CreateOrUpdateAsync(resourceGroup, Guid.NewGuid().ToString(), parameters);
            _logTracer.Info($"Successfully created auto scale resource {autoScaleResource.Id} for {resourceId}");
            return OneFuzzResult<Azure.Management.Monitor.Models.AutoscaleSettingResource>.Ok(autoScaleResource);
        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<Azure.Management.Monitor.Models.AutoscaleSettingResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"unable to create auto scale resource for resource: {resourceId} with profile: {profile}");
        }
    }


    //TODO: Do this using bicep template
    public Azure.Management.Monitor.Models.AutoscaleProfile CreateAutoScaleProfile(
    string queueUri,
    long minAmount,
    long maxAmount,
    long defaultAmount,
    int scaleOutAmount,
    double scaleOutCooldownMinutes,
    int scaleInAmount,
    double scaleInCooldownMinutes) {

        var rules = new[] {
            //Scale out
            new Azure.Management.Monitor.Models.ScaleRule(
                new Azure.Management.Monitor.Models.MetricTrigger(
                    metricName: "ApproximateMessageCount",
                    metricResourceUri: queueUri,
                    //check every 15 minutes
                    timeGrain: TimeSpan.FromMinutes(15.0),
                    // the average amount of messages there are in the pool queue
                    timeAggregation: Azure.Management.Monitor.Models.TimeAggregationType.Average,
                    statistic: Azure.Management.Monitor.Models.MetricStatisticType.Count,
                    //over the past 15 minutes
                    timeWindow: TimeSpan.FromMinutes(15.0),
                    //when there is more than 1 message in the pool queue
                    operatorProperty: Azure.Management.Monitor.Models.ComparisonOperationType.GreaterThanOrEqual,
                    threshold: 1,
                    dividePerInstance: false
                ),
                new Azure.Management.Monitor.Models.ScaleAction(
                    direction: Azure.Management.Monitor.Models.ScaleDirection.Increase,
                    type: Azure.Management.Monitor.Models.ScaleType.ChangeCount,
                    cooldown: TimeSpan.FromMinutes(scaleOutCooldownMinutes)
                ) { Value = scaleOutAmount.ToString()}
            ),
            //Scale In
            new Azure.Management.Monitor.Models.ScaleRule(
                new Azure.Management.Monitor.Models.MetricTrigger(
                    metricName: "ApproximateMessageCount",
                    metricResourceUri: queueUri,
                    //check every 10 minutes
                    timeGrain: TimeSpan.FromMinutes(10.0),
                    // the average amount of messages there are in the pool queue
                    timeAggregation: Azure.Management.Monitor.Models.TimeAggregationType.Average,
                    statistic: Azure.Management.Monitor.Models.MetricStatisticType.Count,
                    //over the past 10 minutes
                    timeWindow: TimeSpan.FromMinutes(10.0),
                    //when there is more than 1 message in the pool queue
                    operatorProperty: Azure.Management.Monitor.Models.ComparisonOperationType.Equals,
                    threshold: 0,
                    dividePerInstance: false
                ),
                new Azure.Management.Monitor.Models.ScaleAction(
                    direction: Azure.Management.Monitor.Models.ScaleDirection.Decrease,
                    type: Azure.Management.Monitor.Models.ScaleType.ChangeCount,
                    cooldown: TimeSpan.FromMinutes(scaleInCooldownMinutes)
                ) { Value = scaleInAmount.ToString()}
            )
        };

        // Auto scale tuning guidance:
        //https://docs.microsoft.com/en-us/azure/architecture/best-practices/auto-scaling
        return new Azure.Management.Monitor.Models.AutoscaleProfile(Guid.NewGuid().ToString(), new Azure.Management.Monitor.Models.ScaleCapacity(minAmount.ToString(), maxAmount.ToString(), defaultAmount.ToString()), rules);
    }


    public Azure.Management.Monitor.Models.AutoscaleProfile DeafaultAutoScaleProfile(string queueUri, long scaleSetSize) {
        return CreateAutoScaleProfile(queueUri, 1L, scaleSetSize, scaleSetSize, 1, 10.0, 1, 5.0);
    }


    private async Async.Task<OneFuzzResult<Azure.Management.Monitor.Models.DiagnosticSettingsResource>> SetupAutoScaleDiagnostics(string autoScaleResourceUri, string autoScaleResourceName, string logAnalyticsWorkspaceId) {
        var logSettings = new Azure.Management.Monitor.Models.LogSettings(true) { Category = "alllogs", RetentionPolicy = new Azure.Management.Monitor.Models.RetentionPolicy(true, 30) };
        var monitorManagementClient = _context.LogAnalytics.GetMonitorManagementClient();

        try {
            var parameters = new Azure.Management.Monitor.Models.DiagnosticSettingsResource();
            parameters.Logs.Add(logSettings);
            parameters.WorkspaceId = logAnalyticsWorkspaceId;
            var diagnostics = await monitorManagementClient.DiagnosticSettings.CreateOrUpdateAsync(resourceUri: autoScaleResourceUri, name: $"{autoScaleResourceName}-diagnostics", parameters: parameters);
            return OneFuzzResult.Ok(diagnostics);
        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<Azure.Management.Monitor.Models.DiagnosticSettingsResource>.Error(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"unable to setup diagnostics for auto-scale resource: {autoScaleResourceUri}"
                );
        }
    }
}
