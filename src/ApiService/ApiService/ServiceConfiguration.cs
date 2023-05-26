using System.Reflection;
using Azure.Core;

namespace Microsoft.OneFuzz.Service;

public enum LogDestination {
    Console,
    AppInsights,
}


public interface IServiceConfig {
    #region Parameters for logging & application insights
    public LogDestination[] LogDestinations { get; }
    public ApplicationInsights.DataContracts.SeverityLevel LogSeverityLevel { get; }
    public string? ApplicationInsightsAppId { get; }
    public string? ApplicationInsightsInstrumentationKey { get; }
    #endregion

    #region Parameters for feature flags
    public string? AppConfigurationEndpoint { get; }
    public string? AppConfigurationConnectionString { get; }
    #endregion

    #region Auth parameters for CLI app
    public string? CliAppId { get; }
    public string? Authority { get; }
    public string? TenantDomain { get; }
    public string? MultiTenantDomain { get; }
    #endregion

    public ResourceIdentifier OneFuzzResourceGroup { get; }
    public ResourceIdentifier OneFuzzDataStorage { get; }
    public ResourceIdentifier OneFuzzFuncStorage { get; }
    public Uri OneFuzzInstance { get; }
    public string OneFuzzInstanceName { get; }
    public Uri? OneFuzzEndpoint { get; }
    public string OneFuzzKeyvault { get; }
    public string? OneFuzzMonitor { get; }
    public string? OneFuzzOwner { get; }
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
    private static readonly string _oneFuzzVersion =
        Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Unable to read OneFuzz version from assembly");

    public ServiceConfiguration() {
#if DEBUG
        LogDestinations = new LogDestination[] { LogDestination.AppInsights, LogDestination.Console };
#else
        LogDestinations = new LogDestination[] { LogDestination.AppInsights };
#endif
    }

    private static string? GetEnv(string name) {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static string MustGetEnv(string name)
        => GetEnv(name) ?? throw new InvalidOperationException($"Environment variable {name} is required to be set");

    //TODO: Add environment variable to control where to write logs to
    public LogDestination[] LogDestinations { get; private set; }

    //TODO: Get this from Environment variable
    public ApplicationInsights.DataContracts.SeverityLevel LogSeverityLevel => ApplicationInsights.DataContracts.SeverityLevel.Verbose;

    public string? ApplicationInsightsAppId { get; } = GetEnv("APPINSIGHTS_APPID");

    public string? ApplicationInsightsInstrumentationKey { get; } = GetEnv("APPINSIGHTS_INSTRUMENTATIONKEY");

    public string? AppConfigurationEndpoint { get; } = GetEnv("APPCONFIGURATION_ENDPOINT");

    public string? AppConfigurationConnectionString { get; } = GetEnv("APPCONFIGURATION_CONNECTION_STRING");

    public string? CliAppId { get; } = GetEnv("CLI_APP_ID");

    public string? Authority { get; } = GetEnv("AUTHORITY");

    public string? TenantDomain { get; } = GetEnv("TENANT_DOMAIN");

    public string? MultiTenantDomain { get; } = GetEnv("MULTI_TENANT_DOMAIN");

    public ResourceIdentifier OneFuzzDataStorage { get; } = new(MustGetEnv("ONEFUZZ_DATA_STORAGE"));

    public ResourceIdentifier OneFuzzFuncStorage { get; } = new(MustGetEnv("ONEFUZZ_FUNC_STORAGE"));

    public Uri OneFuzzInstance { get; } = new Uri(MustGetEnv("ONEFUZZ_INSTANCE"));

    public string OneFuzzInstanceName { get; } = MustGetEnv("ONEFUZZ_INSTANCE_NAME");

    public Uri? OneFuzzEndpoint { get; } = GetEnv("ONEFUZZ_ENDPOINT") is string value ? new Uri(value) : null;

    public string OneFuzzKeyvault { get; } = MustGetEnv("ONEFUZZ_KEYVAULT");

    public string? OneFuzzMonitor { get; } = GetEnv("ONEFUZZ_MONITOR");

    public string? OneFuzzOwner { get; } = GetEnv("ONEFUZZ_OWNER");

    public ResourceIdentifier OneFuzzResourceGroup { get; } = new(MustGetEnv("ONEFUZZ_RESOURCE_GROUP"));

    public string? OneFuzzTelemetry { get; } = GetEnv("ONEFUZZ_TELEMETRY");

    public string OneFuzzVersion { get; } = GetEnv("ONEFUZZ_VERSION") ?? _oneFuzzVersion;

    public string? OneFuzzAllowOutdatedAgent { get; } = GetEnv("ONEFUZZ_ALLOW_OUTDATED_AGENT");

    public string OneFuzzStoragePrefix => ""; // in production we never prefix the tables
}
