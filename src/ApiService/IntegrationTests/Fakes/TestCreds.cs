using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.OneFuzz.Service;
using Task = System.Threading.Tasks.Task;

namespace IntegrationTests.Fakes;

sealed class TestCreds : ICreds {

    private readonly Guid _subscriptionId;
    private readonly string _resourceGroup;
    private readonly Region _region;
    private readonly string _instanceName;


    public TestCreds(Guid subscriptionId, string resourceGroup, Region region, string instanceName) {
        _subscriptionId = subscriptionId;
        _resourceGroup = resourceGroup;
        _region = region;
        _instanceName = instanceName;
    }

    public ArmClient ArmClient => null!;
    // we have to return something in some test cases, even if it isn’t used

    public Task<Region> GetBaseRegion() => Task.FromResult(_region);
    public Task<IReadOnlyList<Region>> GetRegions() => Task.FromResult<IReadOnlyList<Region>>(new[] { _region });

    public string GetBaseResourceGroup() => _resourceGroup;

    public string GetSubscription() => _subscriptionId.ToString();

    public Uri GetInstanceUrl() => new("https://example.com/api/");

    public DefaultAzureCredential GetIdentity() {
        throw new NotImplementedException();
    }

    public string GetInstanceName() {
        return _instanceName;
    }

    public ResourceGroupResource GetResourceGroupResource() {
        throw new NotImplementedException();
    }

    public SubscriptionResource GetSubscriptionResource() {
        throw new NotImplementedException();
    }

    public ResourceIdentifier GetResourceGroupResourceIdentifier() {
        throw new NotImplementedException();
    }

    public Task<Guid> GetScalesetPrincipalId() {
        throw new NotImplementedException();
    }

    public Task<T> QueryMicrosoftGraph<T>(HttpMethod method, string resource) {
        throw new NotImplementedException();
    }

    public GenericResource ParseResourceId(string resourceId) {
        throw new NotImplementedException();
    }

    public GenericResource ParseResourceId(ResourceIdentifier resourceId) {
        throw new NotImplementedException();
    }


    public Task<GenericResource> GetData(GenericResource resource) {
        throw new NotImplementedException();
    }

    public ResourceIdentifier GetScalesetIdentityResourcePath() {
        throw new NotImplementedException();
    }
}
