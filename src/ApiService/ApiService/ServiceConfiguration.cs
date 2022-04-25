namespace Microsoft.OneFuzz.Service;

public enum LogDestination
{
    Console,
    AppInsights,
}


public interface IServiceConfig
{
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

    public string? OneFuzzResourceGroup { get; }
    public string? OneFuzzTelemetry { get; }

    public string OnefuzzVersion { get; }
}

public class ServiceConfiguration : IServiceConfig
{

    public ServiceConfiguration()
    {
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
    public string OnefuzzVersion { get => Environment.GetEnvironmentVariable("ONEFUZZ_VERSION") ?? "0.0.0"; }
}