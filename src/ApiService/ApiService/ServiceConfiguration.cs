﻿using System.Reflection;

namespace Microsoft.OneFuzz.Service;

public enum LogDestination {
    Console,
    AppInsights,
}


public interface IServiceConfig {
    public LogDestination[] LogDestinations { get; set; }

    public ApplicationInsights.DataContracts.SeverityLevel LogSeverityLevel { get; }

    public string? ApplicationInsightsAppId { get; }
    public string? ApplicationInsightsInstrumentationKey { get; }
    public string? AzureSignalRConnectionString { get; }
    public string? AzureSignalRServiceTransportType { get; }

    public string? AzureWebJobDisableHomePage { get; }
    public string? AzureWebJobStorage { get; }

    public string? DiagnosticsAzureBlobContainerSasUrl { get; }
    public string? DiagnosticsAzureBlobRetentionDays { get; }

    public string? MultiTenantDomain { get; }
    public string? OneFuzzDataStorage { get; }
    public string? OneFuzzFuncStorage { get; }
    public string? OneFuzzInstance { get; }
    public string? OneFuzzInstanceName { get; }
    public string? OneFuzzKeyvault { get; }

    public string? OneFuzzMonitor { get; }
    public string? OneFuzzOwner { get; }

    public string OneFuzzNodeDisposalStrategy { get; }

    public string? OneFuzzResourceGroup { get; }
    public string? OneFuzzTelemetry { get; }

    public string OneFuzzVersion { get; }

    public string? OneFuzzAllowOutdatedAgent { get; }

    // Prefix to add to the name of any tables & containers created. This allows
    // multiple instances to run against the same storage account, which
    // is useful for things like integration testing.
    public string OneFuzzStoragePrefix { get; }
}

public class ServiceConfiguration : IServiceConfig {

    // Version is baked into the assembly by the build process:
    private static readonly string? _oneFuzzVersion =
        Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public ServiceConfiguration() {
#if DEBUG
        LogDestinations = new LogDestination[] { LogDestination.AppInsights, LogDestination.Console };
#else
        LogDestinations = new LogDestination[] { LogDestination.AppInsights };
#endif
    }

    //TODO: Add environment variable to control where to write logs to
    public LogDestination[] LogDestinations { get; set; }

    //TODO: Get this from Environment variable
    public ApplicationInsights.DataContracts.SeverityLevel LogSeverityLevel => ApplicationInsights.DataContracts.SeverityLevel.Verbose;

    public string? ApplicationInsightsAppId => Environment.GetEnvironmentVariable("APPINSIGHTS_APPID");
    public string? ApplicationInsightsInstrumentationKey => Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");

    public string? AzureSignalRConnectionString => Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
    public string? AzureSignalRServiceTransportType => Environment.GetEnvironmentVariable("AzureSignalRServiceTransportType");

    public string? AzureWebJobDisableHomePage { get => Environment.GetEnvironmentVariable("AzureWebJobsDisableHomepage"); }
    public string? AzureWebJobStorage { get => Environment.GetEnvironmentVariable("AzureWebJobsStorage"); }

    public string? DiagnosticsAzureBlobContainerSasUrl { get => Environment.GetEnvironmentVariable("DIAGNOSTICS_AZUREBLOBCONTAINERSASURL"); }
    public string? DiagnosticsAzureBlobRetentionDays { get => Environment.GetEnvironmentVariable("DIAGNOSTICS_AZUREBLOBRETENTIONINDAYS"); }

    public string? MultiTenantDomain { get => Environment.GetEnvironmentVariable("MULTI_TENANT_DOMAIN"); }

    public string? OneFuzzDataStorage { get => Environment.GetEnvironmentVariable("ONEFUZZ_DATA_STORAGE"); }
    public string? OneFuzzFuncStorage { get => Environment.GetEnvironmentVariable("ONEFUZZ_FUNC_STORAGE"); }
    public string? OneFuzzInstance { get => Environment.GetEnvironmentVariable("ONEFUZZ_INSTANCE"); }
    public string? OneFuzzInstanceName { get => Environment.GetEnvironmentVariable("ONEFUZZ_INSTANCE_NAME"); }
    public string? OneFuzzKeyvault { get => Environment.GetEnvironmentVariable("ONEFUZZ_KEYVAULT"); }
    public string? OneFuzzMonitor { get => Environment.GetEnvironmentVariable("ONEFUZZ_MONITOR"); }
    public string? OneFuzzOwner { get => Environment.GetEnvironmentVariable("ONEFUZZ_OWNER"); }
    public string? OneFuzzResourceGroup { get => Environment.GetEnvironmentVariable("ONEFUZZ_RESOURCE_GROUP"); }
    public string? OneFuzzTelemetry { get => Environment.GetEnvironmentVariable("ONEFUZZ_TELEMETRY"); }

    public string OneFuzzVersion {
        get {
            // version can be overridden by config:
            return Environment.GetEnvironmentVariable("ONEFUZZ_VERSION")
                ?? _oneFuzzVersion
                ?? throw new InvalidOperationException("Unable to read OneFuzz version from assembly");
        }
    }

    public string? OneFuzzAllowOutdatedAgent => Environment.GetEnvironmentVariable("ONEFUZZ_ALLOW_OUTDATED_AGENT");

    public string OneFuzzNodeDisposalStrategy { get => Environment.GetEnvironmentVariable("ONEFUZZ_NODE_DISPOSAL_STRATEGY") ?? "scale_in"; }
    public string OneFuzzStoragePrefix => ""; // in production we never prefix the tables
}
