using System.Net.Http;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.OneFuzz.Service;

public interface ICreds {
    public DefaultAzureCredential GetIdentity();

    public string GetSubscription();

    public string GetBaseResourceGroup();

    public ResourceIdentifier GetResourceGroupResourceIdentifier();

    public string GetInstanceName();

    public ArmClient ArmClient { get; }

    public ResourceGroupResource GetResourceGroupResource();

    public SubscriptionResource GetSubscriptionResource();

    public Async.Task<string> GetBaseRegion();

    public Uri GetInstanceUrl();
    public Async.Task<Guid> GetScalesetPrincipalId();
    public GenericResource ParseResourceId(string resourceId);
    public GenericResource ParseResourceId(ResourceIdentifier resourceId);
    public Async.Task<GenericResource> GetData(GenericResource resource);
    Async.Task<IReadOnlyList<string>> GetRegions();
    public ResourceIdentifier GetScalesetIdentityResourcePath();
}

public sealed class Creds : ICreds {
    private readonly ArmClient _armClient;
    private readonly DefaultAzureCredential _azureCredential;
    private readonly IServiceConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    public ArmClient ArmClient => _armClient;

    public Creds(IServiceConfig config, IHttpClientFactory httpClientFactory, IMemoryCache cache) {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _azureCredential = new DefaultAzureCredential();
        _armClient = new ArmClient(this.GetIdentity(), this.GetSubscription());

    }

    public DefaultAzureCredential GetIdentity() {
        return _azureCredential;
    }

    public string GetSubscription() {
        var storageResourceId = _config.OneFuzzDataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        return storageResourceId.SubscriptionId
            ?? throw new Exception("OneFuzzDataStorage did not have subscription ID");
    }

    public string GetBaseResourceGroup() {
        var storageResourceId = _config.OneFuzzDataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        return storageResourceId.ResourceGroupName
            ?? throw new Exception("OneFuzzDataStorage did not have resource group name");
    }

    public ResourceIdentifier GetResourceGroupResourceIdentifier() {
        var resourceId = _config.OneFuzzResourceGroup
            ?? throw new System.Exception("Resource group env var is not present");
        return new ResourceIdentifier(resourceId);
    }

    public string GetInstanceName() {
        var instanceName = _config.OneFuzzInstanceName
            ?? throw new System.Exception("Instance Name env var is not present");

        return instanceName;
    }

    public ResourceGroupResource GetResourceGroupResource() {
        var resourceId = GetResourceGroupResourceIdentifier();
        return ArmClient.GetResourceGroupResource(resourceId);
    }

    public SubscriptionResource GetSubscriptionResource() {
        var id = SubscriptionResource.CreateResourceIdentifier(GetSubscription());
        return ArmClient.GetSubscriptionResource(id);
    }

    public Async.Task<string> GetBaseRegion() {
        return _cache.GetOrCreateAsync(nameof(GetBaseRegion), async _ => {
            var rg = await ArmClient.GetResourceGroupResource(GetResourceGroupResourceIdentifier()).GetAsync();
            if (rg.GetRawResponse().IsError) {
                throw new Exception($"Failed to get base region due to [{rg.GetRawResponse().Status}] {rg.GetRawResponse().ReasonPhrase}");
            }
            return rg.Value.Data.Location.Name;
        });
    }

    public Uri GetInstanceUrl()
        // TODO: remove -net when promoted to main version
        => new($"https://{GetInstanceName()}-net.azurewebsites.net");

    public record ScaleSetIdentity(string principalId);

    public Async.Task<Guid> GetScalesetPrincipalId() {
        return _cache.GetOrCreateAsync(nameof(GetScalesetPrincipalId), async entry => {
            var path = GetScalesetIdentityResourcePath();
            var uid = ArmClient.GetGenericResource(path);

            var resource = await uid.GetAsync();
            var principalId = resource.Value.Data.Properties.ToObjectFromJson<ScaleSetIdentity>().principalId;
            return Guid.Parse(principalId);
        });
    }

    public ResourceIdentifier GetScalesetIdentityResourcePath() {
        var scalesetIdName = $"{GetInstanceName()}-scalesetid";
        var resourceGroupPath = $"/subscriptions/{GetSubscription()}/resourceGroups/{GetBaseResourceGroup()}/providers";

        return new ResourceIdentifier($"{resourceGroupPath}/Microsoft.ManagedIdentity/userAssignedIdentities/{scalesetIdName}");
    }

    public GenericResource ParseResourceId(ResourceIdentifier resourceId) {
        return ArmClient.GetGenericResource(resourceId);
    }

    public GenericResource ParseResourceId(string resourceId) {
        return ArmClient.GetGenericResource(new ResourceIdentifier(resourceId));
    }

    public async Async.Task<GenericResource> GetData(GenericResource resource) {
        if (!resource.HasData) {
            return await resource.GetAsync();
        }
        return resource;
    }

    public Task<IReadOnlyList<string>> GetRegions()
        => _cache.GetOrCreateAsync<IReadOnlyList<string>>(
            nameof(Creds) + "." + nameof(GetRegions),
            async entry => {
                // cache for one day
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                var subscriptionId = SubscriptionResource.CreateResourceIdentifier(GetSubscription());
                return await ArmClient.GetSubscriptionResource(subscriptionId)
                    .GetLocationsAsync()
                    .Select(x => x.Name)
                    .ToListAsync();
            });

}


class GraphQueryException : Exception {
    public GraphQueryException(string? message) : base(message) {
    }
}
