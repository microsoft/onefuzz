using System;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.OneFuzz.Service;

namespace Tests.Fakes;

public sealed class TestServiceConfiguration : IServiceConfig {
    public TestServiceConfiguration(string tablePrefix) {
        OneFuzzStoragePrefix = tablePrefix;
    }

    public string OneFuzzStoragePrefix { get; }

    public string? OneFuzzFuncStorage { get; } = "UNUSED_ACCOUNT_ID"; // test implementations do not use this

    public string OneFuzzVersion => "9999.0.0"; // very big version to pass any >= checks

    public string? ApplicationInsightsAppId { get; set; } = "TestAppInsightsAppId";

    public string? ApplicationInsightsInstrumentationKey { get; set; } = "TestAppInsightsInstrumentationKey";

    public string? OneFuzzInstanceName => "UnitTestInstance";

    // -- Remainder not implemented --

    public LogDestination[] LogDestinations { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public SeverityLevel LogSeverityLevel => throw new System.NotImplementedException();


    public string? AzureSignalRConnectionString => throw new System.NotImplementedException();

    public string? AzureSignalRServiceTransportType => throw new System.NotImplementedException();

    public string? AzureWebJobDisableHomePage => throw new System.NotImplementedException();

    public string? AzureWebJobStorage => throw new System.NotImplementedException();

    public string? DiagnosticsAzureBlobContainerSasUrl => throw new System.NotImplementedException();

    public string? DiagnosticsAzureBlobRetentionDays => throw new System.NotImplementedException();

    public string? MultiTenantDomain => throw new System.NotImplementedException();

    public string? OneFuzzInstance => throw new System.NotImplementedException();

    public string? OneFuzzKeyvault => throw new System.NotImplementedException();

    public string? OneFuzzMonitor => throw new System.NotImplementedException();

    public string? OneFuzzOwner => throw new System.NotImplementedException();

    public string OneFuzzNodeDisposalStrategy => throw new System.NotImplementedException();

    public string? OneFuzzTelemetry => throw new System.NotImplementedException();

    public string? OneFuzzDataStorage => throw new NotImplementedException();

    public string? OneFuzzResourceGroup => throw new NotImplementedException();
}
