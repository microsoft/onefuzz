using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Monitor.Models;

namespace Microsoft.OneFuzz.Service;

public interface IAutoScaleOperations {

    public Async.Task<ResultVoid<(int, string)>> Insert(AutoScale autoScale);

    public Async.Task<AutoScale?> GetSettingsForScaleset(Guid scalesetId);

    AutoscaleProfile CreateAutoScaleProfile(
        string queueUri,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        long scaleOutAmount,
        double scaleOutCooldownMinutes,
        long scaleInAmount,
        double scaleInCooldownMinutes);

    AutoscaleProfile DefaultAutoScaleProfile(string queueUri, long scaleSetSize);
    Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, AutoscaleProfile autoScaleProfile);

    OneFuzzResult<AutoscaleSettingResource?> GetAutoscaleSettings(Guid vmss);

    Async.Task<OneFuzzResultVoid> UpdateAutoscale(AutoscaleSettingData autoscale);

    Async.Task<OneFuzzResult<AutoscaleProfile>> GetAutoScaleProfile(Guid scalesetId);

    Async.Task<AutoScale> Update(
        Guid scalesetId,
        long minAmount,
        long maxAmount,
        long defaultAmount,
        long scaleOutAmount,
        long scaleOutCooldown,
        long scaleInAmount,
        long scaleInCooldown);
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
                ScaleOutCooldown: scaleOutCooldown,
                ScaleInAmount: scaleInAmount,
                ScaleInCooldown: scaleInCooldown
                );

        var r = await Insert(entry);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to save auto-scale record for scaleset ID: {scalesetId}, minAmount: {minAmount}, maxAmount: {maxAmount}, defaultAmount: {defaultAmount}, scaleOutAmount: {scaleOutAmount}, scaleOutCooldown: {scaleOutCooldown}, scaleInAmount: {scaleInAmount}, scaleInCooldown: {scaleInCooldown}");
        }
        return entry;
    }

    public async Async.Task<AutoScale?> GetSettingsForScaleset(Guid scalesetId) {
        try {
            var autoscale = await GetEntityAsync(scalesetId.ToString(), scalesetId.ToString());
            return autoscale;
        } catch (Exception ex) {
            _logTracer.Exception(ex, "Failed to get auto-scale entity");
            return null;
        }
    }

    public async Async.Task<OneFuzzResult<AutoscaleProfile>> GetAutoScaleProfile(Guid scalesetId) {
        _logTracer.Info($"getting scaleset for existing auto-scale resources {scalesetId}");
        var settings = _context.Creds.GetResourceGroupResource().GetAutoscaleSettings();
        if (settings is null) {
            return OneFuzzResult<AutoscaleProfile>.Error(ErrorCode.INVALID_CONFIGURATION, $"could not find any auto-scale settings for the resource group");
        } else {
            await foreach (var setting in settings.GetAllAsync()) {

                if (setting.Data.TargetResourceId.EndsWith(scalesetId.ToString())) {
                    if (setting.Data.Profiles.Count != 1) {
                        return OneFuzzResult<AutoscaleProfile>.Error(ErrorCode.INVALID_CONFIGURATION, $"found {setting.Data.Profiles.Count} auto-scale profiles for {scalesetId}");
                    } else {
                        return OneFuzzResult<AutoscaleProfile>.Ok(setting.Data.Profiles.First());
                    }
                }
            }
        }
        return OneFuzzResult<AutoscaleProfile>.Error(ErrorCode.INVALID_CONFIGURATION, $"could not find auto-scale settings for scaleset {scalesetId}");
    }

    public async Async.Task<OneFuzzResultVoid> AddAutoScaleToVmss(Guid vmss, AutoscaleProfile autoScaleProfile) {
        _logTracer.Info($"Checking scaleset {vmss} for existing auto scale resource");

        var existingAutoScaleResource = GetAutoscaleSettings(vmss);

        if (!existingAutoScaleResource.IsOk) {
            return OneFuzzResultVoid.Error(existingAutoScaleResource.ErrorV);
        }

        if (existingAutoScaleResource.OkV != null) {
            return OneFuzzResultVoid.Ok;
        }

        var autoScaleResource = await CreateAutoScaleResourceFor(vmss, await _context.Creds.GetBaseRegion(), autoScaleProfile);
        if (!autoScaleResource.IsOk) {
            return OneFuzzResultVoid.Error(autoScaleResource.ErrorV);
        }
        var workspaceId = _context.LogAnalytics.GetWorkspaceId().ToString();
        _logTracer.Info($"Setting up diagnostics for id: {autoScaleResource.OkV.Id!} with name: {autoScaleResource.OkV.Data.Name} and workspace id: {workspaceId}");

        var diagnosticsResource = await SetupAutoScaleDiagnostics(autoScaleResource.OkV, workspaceId);
        if (!diagnosticsResource.IsOk) {
            return OneFuzzResultVoid.Error(diagnosticsResource.ErrorV);
        }

        return OneFuzzResultVoid.Ok;
    }
    private async Async.Task<OneFuzzResult<AutoscaleSettingResource>> CreateAutoScaleResourceFor(Guid resourceId, Region location, AutoscaleProfile profile) {
        _logTracer.Info($"Creating auto-scale resource for: {resourceId}");

        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        var subscription = _context.Creds.GetSubscription();

        var scalesetUri = $"/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{resourceId}";
        var parameters = new AutoscaleSettingData(location, new[] { profile }) {
            TargetResourceId = scalesetUri,
            Enabled = true
        };

        try {
            var autoScaleSettings = _context.Creds.GetResourceGroupResource().GetAutoscaleSettings();
            var autoScaleResource = await autoScaleSettings.CreateOrUpdateAsync(WaitUntil.Started, Guid.NewGuid().ToString(), parameters);

            if (autoScaleResource != null && autoScaleResource.HasValue) {
                _logTracer.Info($"Successfully created auto scale resource {autoScaleResource.Value.Id} for {resourceId}");
                return OneFuzzResult<AutoscaleSettingResource>.Ok(autoScaleResource.Value);
            }

            return OneFuzzResult<AutoscaleSettingResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"Could not get auto scale resource value after creating for {resourceId}"
            );
        } catch (RequestFailedException ex) when (ex.Status == 409 && ex.Message.Contains("\"code\":\"SettingAlreadyExists\"")) {
            var existingAutoScaleResource = GetAutoscaleSettings(resourceId);
            if (existingAutoScaleResource.IsOk) {
                _logTracer.Info($"Successfully created auto scale resource {existingAutoScaleResource.OkV!.Data.Id} for {resourceId}");
                return OneFuzzResult<AutoscaleSettingResource>.Ok(existingAutoScaleResource.OkV!);
            } else {
                return existingAutoScaleResource.ErrorV;
            }

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


    public AutoscaleProfile DefaultAutoScaleProfile(string queueUri, long scaleSetSize) {
        return CreateAutoScaleProfile(queueUri, 1L, scaleSetSize, scaleSetSize, 1, 10.0, 1, 5.0);
    }


    private async Async.Task<OneFuzzResult<DiagnosticSettingsResource>> SetupAutoScaleDiagnostics(AutoscaleSettingResource autoscaleSettingResource, string logAnalyticsWorkspaceId) {
        try {
            // TODO: we are missing CategoryGroup = "allLogs", we cannot set it since current released dotnet SDK is missing the field
            // The field is there in github though, so need to update this code once that code is released:
            // https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.ResourceManager.Monitor/src/Generated/Models/LogSettings.cs
            // But setting logs one by one works the same as "allLogs" being set...
            var logSettings1 = new LogSettings(true) { RetentionPolicy = new RetentionPolicy(true, 30), Category = "AutoscaleEvaluations" };
            var logSettings2 = new LogSettings(true) { RetentionPolicy = new RetentionPolicy(true, 30), Category = "AutoscaleScaleActions" };

            var parameters = new DiagnosticSettingsData {
                WorkspaceId = logAnalyticsWorkspaceId
            };
            parameters.Logs.Add(logSettings1);
            parameters.Logs.Add(logSettings2);

            var diagnostics = await autoscaleSettingResource.GetDiagnosticSettings().CreateOrUpdateAsync(WaitUntil.Started, $"{autoscaleSettingResource.Data.Name}-diagnostics", parameters);
            if (diagnostics != null && diagnostics.HasValue) {
                return OneFuzzResult.Ok(diagnostics.Value);
            }
            return OneFuzzResult<DiagnosticSettingsResource>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                $"The resulting diagnostics settings resource was null when attempting to create for {autoscaleSettingResource.Id}"
            );
        } catch (Exception ex) {
            _logTracer.Exception(ex);
            return OneFuzzResult<DiagnosticSettingsResource>.Error(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"unable to setup diagnostics for auto-scale resource: {autoscaleSettingResource.Id} and name: {autoscaleSettingResource.Data.Name}"
                );
        }
    }

    public OneFuzzResult<AutoscaleSettingResource?> GetAutoscaleSettings(Guid vmss) {
        _logTracer.Info($"Checking scaleset {vmss} for existing auto scale resource");
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

    public async Async.Task<AutoScale> Update(
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
                ScaleOutCooldown: scaleOutCooldown,
                ScaleInAmount: scaleInAmount,
                ScaleInCooldown: scaleInCooldown
                );

        var r = await Replace(entry);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to replace auto-scale record for scaleset ID: {scalesetId}, minAmount: {minAmount}, maxAmount: {maxAmount}, defaultAmount: {defaultAmount}, scaleOutAmount: {scaleOutAmount}, scaleOutCooldown: {scaleOutCooldown}, scaleInAmount: {scaleInAmount}, scaleInCooldown: {scaleInCooldown}");
        }
        return entry;
    }
}
