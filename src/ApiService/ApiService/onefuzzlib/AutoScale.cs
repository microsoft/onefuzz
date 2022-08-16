using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Monitor.Models;

namespace Microsoft.OneFuzz.Service;

public interface IAutoScaleOperations {
    Async.Task<AutoScale> Create(
        Guid scalesetId,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        long scaleOutAmount,
        long scaleOutCooldown,
        long scaleInAmount,
        long scaleInCooldown);

    Async.Task<AutoScale?> GetSettingsForScaleset(Guid scalesetId);


    AutoscaleProfile CreateAutoScaleProfile(
        string queueUri,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        long scaleOutAmount,
        double scaleOutCooldownMinutes,
        long scaleInAmount,
        double scaleInCooldownMinutes);

    AutoscaleProfile DeafaultAutoScaleProfile(string queueUri, long scaleSetSize);
    Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, AutoscaleProfile autoScaleProfile);

    OneFuzzResult<AutoscaleSettingResource?> GetAutoscaleSettings(Guid vmss);

    Async.Task<OneFuzzResultVoid> UpdateAutoscale(AutoscaleSettingData autoscale);
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
    long scaleOutAmount,
    long scaleOutCooldown,
    long scaleInAmount,
    long scaleInCooldown) {

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


    public async Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, AutoscaleProfile autoScaleProfile) {
        _logTracer.Info($"Checking scaleset {vmss} for existing auto scale resource");

        var existingAutoscaleResource = GetAutoscaleSettings(vmss);
        if (!existingAutoscaleResource.IsOk) {
            return OneFuzzResultVoid.Error(existingAutoscaleResource.ErrorV);
        }

        if (existingAutoscaleResource.OkV != null) {
            _logTracer.Warning($"Scaleset {vmss} already has auto scale resource");
            return OneFuzzResultVoid.Ok;
        }

        var autoScaleResource = await CreateAutoScaleResourceFor(vmss, await _context.Creds.GetBaseRegion(), autoScaleProfile);
        if (!autoScaleResource.IsOk) {
            return OneFuzzResultVoid.Error(autoScaleResource.ErrorV);
        }

        var diagnosticsResource = await SetupAutoScaleDiagnostics(autoScaleResource.OkV.Id!, autoScaleResource.OkV.Data.Name, _context.LogAnalytics.GetWorkspaceId().ToString());
        if (!diagnosticsResource.IsOk) {
            return OneFuzzResultVoid.Error(diagnosticsResource.ErrorV);
        }

        return OneFuzzResultVoid.Ok;
    }
    private async Async.Task<OneFuzzResult<AutoscaleSettingResource>> CreateAutoScaleResourceFor(Guid resourceId, string location, AutoscaleProfile profile) {
        _logTracer.Info($"Creating auto-scale resource for: {resourceId}");

        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        var subscription = _context.Creds.GetSubscription();

        var scalesetUri = $"/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{resourceId}";
        var parameters = new AutoscaleSettingData(location, new[] { profile }) {
            TargetResourceId = scalesetUri,
            Enabled = true
        };

        try {
            var autoScaleResource = await _context.Creds.GetResourceGroupResource().GetAutoscaleSettings()
                .CreateOrUpdateAsync(WaitUntil.Completed, Guid.NewGuid().ToString(), parameters);

            if (autoScaleResource != null && autoScaleResource.HasValue) {
                _logTracer.Info($"Successfully created auto scale resource {autoScaleResource.Id} for {resourceId}");
                return OneFuzzResult<AutoscaleSettingResource>.Ok(autoScaleResource.Value);
            }

            return OneFuzzResult<AutoscaleSettingResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"Could not get auto scale resource value after creating for {resourceId}"
            );
        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<AutoscaleSettingResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"unable to create auto scale resource for resource: {resourceId} with profile: {profile}");
        }
    }


    //TODO: Do this using bicep template
    public AutoscaleProfile CreateAutoScaleProfile(
    string queueUri,
    long minAmount,
    long maxAmount,
    long defaultAmount,
    long scaleOutAmount,
    double scaleOutCooldownMinutes,
    long scaleInAmount,
    double scaleInCooldownMinutes) {

        var rules = new[] {
            //Scale out
            new ScaleRule(
                new MetricTrigger(
                    metricName: "ApproximateMessageCount",
                    metricResourceId: queueUri,
                    //check every 15 minutes
                    timeGrain: TimeSpan.FromMinutes(15.0),
                    statistic: MetricStatisticType.Count,
                    //over the past 15 minutes
                    timeWindow: TimeSpan.FromMinutes(15.0),
                    // the average amount of messages there are in the pool queue
                    timeAggregation: TimeAggregationType.Average,
                    //when there is more than 1 message in the pool queue
                    @operator: ComparisonOperationType.GreaterThanOrEqual,
                    threshold: 1
                ) {
                    DividePerInstance = false
                },
                new ScaleAction(
                    direction: ScaleDirection.Increase,
                    scaleType: ScaleType.ChangeCount,
                    cooldown: TimeSpan.FromMinutes(scaleOutCooldownMinutes)
                ) { Value = scaleOutAmount.ToString()}
            ),
            //Scale In
            new ScaleRule(
                new MetricTrigger(
                    metricName: "ApproximateMessageCount",
                    metricResourceId: queueUri,
                    //check every 10 minutes
                    timeGrain: TimeSpan.FromMinutes(10.0),
                    statistic: MetricStatisticType.Count,
                    //over the past 10 minutes
                    timeWindow: TimeSpan.FromMinutes(10.0),
                    // the average amount of messages there are in the pool queue
                    timeAggregation: TimeAggregationType.Average,
                    //when there is more than 1 message in the pool queue
                    @operator: ComparisonOperationType.EqualsValue,
                    threshold: 0
                ) { DividePerInstance = false},
                new ScaleAction(
                    direction: ScaleDirection.Decrease,
                    scaleType: ScaleType.ChangeCount,
                    cooldown: TimeSpan.FromMinutes(scaleInCooldownMinutes)
                ) { Value = scaleInAmount.ToString()}
            )
        };

        // Auto scale tuning guidance:
        //https://docs.microsoft.com/en-us/azure/architecture/best-practices/auto-scaling
        return new AutoscaleProfile(Guid.NewGuid().ToString(), new ScaleCapacity(minAmount.ToString(), maxAmount.ToString(), defaultAmount.ToString()), rules);
    }


    public AutoscaleProfile DeafaultAutoScaleProfile(string queueUri, long scaleSetSize) {
        return CreateAutoScaleProfile(queueUri, 1L, scaleSetSize, scaleSetSize, 1, 10.0, 1, 5.0);
    }


    private async Async.Task<OneFuzzResult<DiagnosticSettingsResource>> SetupAutoScaleDiagnostics(string autoScaleResourceUri, string autoScaleResourceName, string logAnalyticsWorkspaceId) {
        var logSettings = new LogSettings(true) { Category = "allLogs", RetentionPolicy = new RetentionPolicy(true, 30) };

        try {
            var parameters = new DiagnosticSettingsData {
                WorkspaceId = logAnalyticsWorkspaceId
            };
            parameters.Logs.Add(logSettings);
            var diagnostics = await _context.Creds.GetResourceGroupResource().GetDiagnosticSettings().CreateOrUpdateAsync(WaitUntil.Completed, $"{autoScaleResourceName}-diagnostics", parameters);
            if (diagnostics != null && diagnostics.HasValue) {
                return OneFuzzResult.Ok(diagnostics.Value);
            }
            return OneFuzzResult<DiagnosticSettingsResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"The resulting diagnostics settings resource was null when attempting to create for {autoScaleResourceUri}"
            );
        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<DiagnosticSettingsResource>.Error(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"unable to setup diagnostics for auto-scale resource: {autoScaleResourceUri}"
                );
        }
    }

    public OneFuzzResult<AutoscaleSettingResource?> GetAutoscaleSettings(Guid vmss) {
        _logTracer.Info($"Checking scaleset {vmss} for existing auto scale resource");
        var monitorManagementClient = _context.LogAnalytics.GetMonitorManagementClient();
        var resourceGroup = _context.Creds.GetBaseResourceGroup();

        try {
            var autoscale = _context.Creds.GetResourceGroupResource().GetAutoscaleSettings()
                .ToEnumerable()
                .Where(autoScale => autoScale.Data.TargetResourceId.EndsWith(vmss.ToString()))
                .FirstOrDefault();

            if (autoscale != null) {
                _logTracer.Info($"Found autoscale settings for {vmss}");
                return OneFuzzResult.Ok<AutoscaleSettingResource?>(autoscale);
            }

        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<AutoscaleSettingResource?>.Error(ErrorCode.INVALID_CONFIGURATION, $"Failed to check if scaleset {vmss} already has an autoscale resource");
        }
        return OneFuzzResult.Ok<AutoscaleSettingResource?>(null);
    }

    public async Task<OneFuzzResultVoid> UpdateAutoscale(AutoscaleSettingData autoscale) {
        _logTracer.Info($"Updating auto scale resource: {autoscale.Name}");

        try {
            var newResource = await _context.Creds.GetResourceGroupResource().GetAutoscaleSettings().CreateOrUpdateAsync(
                WaitUntil.Started,
                autoscale.Name,
                autoscale
            );

            _logTracer.Info($"Successfully updated auto scale resource: {autoscale.Name}");
        } catch (RequestFailedException ex) {
            _logTracer.Exception(ex);
            return OneFuzzResultVoid.Error(
                ErrorCode.UNABLE_TO_UPDATE,
                $"unable to update auto scale resource with name: {autoscale.Name} and profile: {JsonSerializer.Serialize(autoscale)}"
            );
        }

        return OneFuzzResultVoid.Ok;
    }

    public static ScaleRule ShutdownScalesetRule(string queueUri) {
        return new ScaleRule(
           // Scale in if there are 0 or more messages in the queue (aka: every time)
           new MetricTrigger(
               "ApproximateMessageCount",
               queueUri,
               // Check every 5 minutes
               new TimeSpan(0, 5, 0),
               MetricStatisticType.Sum,
               // Over the past 10 minutes
               new TimeSpan(0, 5, 0),
               // The average amount of messages there are in the pool queue
               TimeAggregationType.Average,
               ComparisonOperationType.GreaterThanOrEqual,
               0
           ) { DividePerInstance = false },
           new ScaleAction(
               ScaleDirection.Decrease,
               ScaleType.ChangeCount,
               new TimeSpan(0, 5, 0)
           ) { Value = "1" }
       );
    }
}
