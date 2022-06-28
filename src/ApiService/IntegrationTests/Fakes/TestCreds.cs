using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.OneFuzz.Service;
using Task = System.Threading.Tasks.Task;

namespace IntegrationTests.Fakes;

class TestCreds : ICreds {

    private readonly Guid _subscriptionId;
    private readonly string _resourceGroup;
    private readonly string _region;

    public TestCreds(Guid subscriptionId, string resourceGroup, string region) {
        _subscriptionId = subscriptionId;
        _resourceGroup = resourceGroup;
        _region = region;
    }

    public ArmClient ArmClient => null!;
    // we have to return something in some test cases, even if it isn’t used

    public Task<string> GetBaseRegion() => Task.FromResult(_region);

    public string GetBaseResourceGroup() => _resourceGroup;

    public string GetSubscription() => _subscriptionId.ToString();

    public DefaultAzureCredential GetIdentity() {
        throw new NotImplementedException();
    }

    public string GetInstanceName() {
        throw new NotImplementedException();
    }

    public Uri GetInstanceUrl() {
        throw new NotImplementedException();
    }

    public ResourceGroupResource GetResourceGroupResource() {
        throw new NotImplementedException();
    }

    public ResourceIdentifier GetResourceGroupResourceIdentifier() {
        throw new NotImplementedException();
    }

    public Guid GetScalesetPrincipalId() {
        throw new NotImplementedException();
    }
}
