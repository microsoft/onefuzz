using System;
namespace Microsoft.OneFuzz.Service;

public enum LogDestination
{
    Console,
    AppInsights,
}

public static class EnvironmentVariables {

    static EnvironmentVariables() { 
        LogDestinations = new LogDestination[] { LogDestination.AppInsights };
    }

    //TODO: Add environment variable to control where to write logs to
    public static LogDestination[] LogDestinations { get; set; }

    public static class AppInsights {
        public static string? AppId { get => Environment.GetEnvironmentVariable("APPINSIGHTS_APPID"); }
        public static string? InstrumentationKey { get => Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"); }
    }

    public static class AzureSignalR {
        public static string? ConnectionString { get => Environment.GetEnvironmentVariable("AzureSignalRConnectionString"); }
        public static string? ServiceTransportType { get => Environment.GetEnvironmentVariable("AzureSignalRServiceTransportType"); }
    }

    public static class AzureWebJob {
        public static string? DisableHomePage { get => Environment.GetEnvironmentVariable("AzureWebJobsDisableHomepage"); }
        public static string? Storage { get => Environment.GetEnvironmentVariable("AzureWebJobsStorage"); }
    }

    public static class DiagnosticsAzureBlob {
        public static string? ContainerSasUrl { get => Environment.GetEnvironmentVariable("DIAGNOSTICS_AZUREBLOBCONTAINERSASURL"); }
        public static string? RetentionDays { get => Environment.GetEnvironmentVariable("DIAGNOSTICS_AZUREBLOBRETENTIONINDAYS"); }
    }

    public static string? MultiTenantDomain { get => Environment.GetEnvironmentVariable("MULTI_TENANT_DOMAIN"); }

    public static class OneFuzz {
        public static string? DataStorage { get => Environment.GetEnvironmentVariable("ONEFUZZ_DATA_STORAGE"); }
        public static string? FuncStorage { get => Environment.GetEnvironmentVariable("ONEFUZZ_FUNC_STORAGE"); }
        public static string? Instance { get => Environment.GetEnvironmentVariable("ONEFUZZ_INSTANCE"); }
        public static string? InstanceName { get => Environment.GetEnvironmentVariable("ONEFUZZ_INSTANCE_NAME"); }
        public static string? Keyvault { get => Environment.GetEnvironmentVariable("ONEFUZZ_KEYVAULT"); }
        public static string? Monitor { get => Environment.GetEnvironmentVariable("ONEFUZZ_MONITOR"); }
        public static string? Owner { get => Environment.GetEnvironmentVariable("ONEFUZZ_OWNER"); }
        public static string? ResourceGroup { get => Environment.GetEnvironmentVariable("ONEFUZZ_RESOURCE_GROUP"); }
        public static string? Telemetry { get => Environment.GetEnvironmentVariable("ONEFUZZ_TELEMETRY"); }
    }
}