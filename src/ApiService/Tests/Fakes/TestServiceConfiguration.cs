using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.OneFuzz.Service;

namespace Tests.Fakes;

sealed class TestServiceConfiguration : IServiceConfig {
    public TestServiceConfiguration(string tablePrefix, string accountId) {
        OneFuzzTablePrefix = tablePrefix;
        OneFuzzFuncStorage = accountId;
    }

    public string OneFuzzTablePrefix { get; }

    public string? OneFuzzFuncStorage { get; }

    // -- Remainder not implemented --

    public LogDestination[] LogDestinations { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public SeverityLevel LogSeverityLevel => throw new System.NotImplementedException();

    public string? ApplicationInsightsAppId => throw new System.NotImplementedException();

    public string? ApplicationInsightsInstrumentationKey => throw new System.NotImplementedException();

    public string? AzureSignalRConnectionString => throw new System.NotImplementedException();

    public string? AzureSignalRServiceTransportType => throw new System.NotImplementedException();

    public string? AzureWebJobDisableHomePage => throw new System.NotImplementedException();

    public string? AzureWebJobStorage => throw new System.NotImplementedException();

    public string? DiagnosticsAzureBlobContainerSasUrl => throw new System.NotImplementedException();

    public string? DiagnosticsAzureBlobRetentionDays => throw new System.NotImplementedException();

    public string? MultiTenantDomain => throw new System.NotImplementedException();

    public string? OneFuzzDataStorage => throw new System.NotImplementedException();


    public string? OneFuzzInstance => throw new System.NotImplementedException();

    public string? OneFuzzInstanceName => throw new System.NotImplementedException();

    public string? OneFuzzKeyvault => throw new System.NotImplementedException();

    public string? OneFuzzMonitor => throw new System.NotImplementedException();

    public string? OneFuzzOwner => throw new System.NotImplementedException();

    public string OneFuzzNodeDisposalStrategy => throw new System.NotImplementedException();

    public string? OneFuzzResourceGroup => throw new System.NotImplementedException();

    public string? OneFuzzTelemetry => throw new System.NotImplementedException();

    public string OneFuzzVersion => throw new System.NotImplementedException();
}
