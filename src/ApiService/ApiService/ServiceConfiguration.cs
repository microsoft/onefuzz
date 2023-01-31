using System.Reflection;
using Azure.Core;

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
    public string? AppConfigurationEndpoint { get; }
    public string? AppConfigurationConnectionString { get; }
    public string? AzureSignalRConnectionString { get; }
    public string? AzureSignalRServiceTransportType { get; }

    public string? AzureWebJobDisableHomePage { get; }
    public string? AzureWebJobStorage { get; }

    public string? DiagnosticsAzureBlobContainerSasUrl { get; }
    public string? DiagnosticsAzureBlobRetentionDays { get; }
    public string? CliAppId { get; }
    public string? Authority { get; }
    public string? TenantDomain { get; }
    public string? MultiTenantDomain { get; }
    public ResourceIdentifier? OneFuzzDataStorage { get; }
    public ResourceIdentifier? OneFuzzFuncStorage { get; }
    public string? OneFuzzInstance { get; }
    public string? OneFuzzInstanceName { get; }
    public string? OneFuzzEndpoint { get; }
    public string? OneFuzzKeyvault { get; }

    public string? OneFuzzMonitor { get; }
    public string? OneFuzzOwner { get; }

    public string? OneFuzzResourceGroup { get; }
    public string? OneFuzzTelemetry { get; }

    public string OneFuzzVersion { get; }

    public string? OneFuzzAllowOutdatedAgent { get; }

    // Prefix to add to the name of any tables & containers created. This allows
    // multiple instances to run against the same storage account, which
    // is useful for things like integration testing.
    public string OneFuzzStoragePrefix { get; }

    public Uri OneFuzzBaseAddress { get; }
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

    private static string? GetEnv(string name) {
        var v = Environment.GetEnvironmentVariable(name);
        if (String.IsNullOrEmpty(v))
            return null;

        return v;
    }

    //TODO: Add environment variable to control where to write logs to
    public LogDestination[] LogDestinations { get; set; }

    //TODO: Get this from Environment variable
    public ApplicationInsights.DataContracts.SeverityLevel LogSeverityLevel => ApplicationInsights.DataContracts.SeverityLevel.Verbose;

    public string? ApplicationInsightsAppId => GetEnv("APPINSIGHTS_APPID");
    public string? ApplicationInsightsInstrumentationKey => GetEnv("APPINSIGHTS_INSTRUMENTATIONKEY");

    public string? AppConfigurationEndpoint => GetEnv("APPCONFIGURATION_ENDPOINT");

    public string? AppConfigurationConnectionString => GetEnv("APPCONFIGURATION_CONNECTION_STRING");

    public string? AzureSignalRConnectionString => GetEnv("AzureSignalRConnectionString");
    public string? AzureSignalRServiceTransportType => GetEnv("AzureSignalRServiceTransportType");

    public string? AzureWebJobDisableHomePage { get => GetEnv("AzureWebJobsDisableHomepage"); }
    public string? AzureWebJobStorage { get => GetEnv("AzureWebJobsStorage"); }

    public string? DiagnosticsAzureBlobContainerSasUrl { get => GetEnv("DIAGNOSTICS_AZUREBLOBCONTAINERSASURL"); }
    public string? DiagnosticsAzureBlobRetentionDays { get => GetEnv("DIAGNOSTICS_AZUREBLOBRETENTIONINDAYS"); }
    public string? CliAppId { get => GetEnv("CLI_APP_ID"); }
    public string? Authority { get => GetEnv("AUTHORITY"); }
    public string? TenantDomain { get => GetEnv("TENANT_DOMAIN"); }
    public string? MultiTenantDomain { get => GetEnv("MULTI_TENANT_DOMAIN"); }

    public ResourceIdentifier? OneFuzzDataStorage {
        get {
            var env = GetEnv("ONEFUZZ_DATA_STORAGE");
            return env is null ? null : new ResourceIdentifier(env);
        }
    }

    public ResourceIdentifier? OneFuzzFuncStorage {
        get {
            var env = GetEnv("ONEFUZZ_FUNC_STORAGE");
            return env is null ? null : new ResourceIdentifier(env);
        }
    }

    public string? OneFuzzInstance { get => GetEnv("ONEFUZZ_INSTANCE"); }
    public string? OneFuzzInstanceName { get => GetEnv("ONEFUZZ_INSTANCE_NAME"); }
    public string? OneFuzzEndpoint { get => GetEnv("ONEFUZZ_ENDPOINT"); }
    public string? OneFuzzKeyvault { get => GetEnv("ONEFUZZ_KEYVAULT"); }
    public string? OneFuzzMonitor { get => GetEnv("ONEFUZZ_MONITOR"); }
    public string? OneFuzzOwner { get => GetEnv("ONEFUZZ_OWNER"); }
    public string? OneFuzzResourceGroup { get => GetEnv("ONEFUZZ_RESOURCE_GROUP"); }
    public string? OneFuzzTelemetry { get => GetEnv("ONEFUZZ_TELEMETRY"); }

    public string OneFuzzVersion {
        get {
            // version can be overridden by config:
            return GetEnv("ONEFUZZ_VERSION")
                ?? _oneFuzzVersion
                ?? throw new InvalidOperationException("Unable to read OneFuzz version from assembly");
        }
    }

    public string? OneFuzzAllowOutdatedAgent => GetEnv("ONEFUZZ_ALLOW_OUTDATED_AGENT");
    public string OneFuzzStoragePrefix => ""; // in production we never prefix the tables

    public Uri OneFuzzBaseAddress {
        get {
            var hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            var scheme = Environment.GetEnvironmentVariable("HTTPS") != null ? "https" : "http";
            return new Uri($"{scheme}://{hostName}");
        }
    }
}
