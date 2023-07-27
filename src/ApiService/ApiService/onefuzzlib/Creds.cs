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

    public Async.Task<Region> GetBaseRegion();
    public Async.Task<IReadOnlyList<Region>> GetRegions();

    public Uri GetInstanceUrl();
    public Async.Task<Guid> GetScalesetPrincipalId();
    public GenericResource ParseResourceId(string resourceId);
    public GenericResource ParseResourceId(ResourceIdentifier resourceId);
    public Async.Task<GenericResource> GetData(GenericResource resource);
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
        _armClient = new ArmClient(GetIdentity(), GetSubscription());

    }

    public DefaultAzureCredential GetIdentity() => _azureCredential;

    public string GetSubscription() => _config.OneFuzzDataStorage.SubscriptionId
            ?? throw new InvalidOperationException("OneFuzzDataStorage did not have subscription ID");

    public string GetBaseResourceGroup() => _config.OneFuzzDataStorage.ResourceGroupName
            ?? throw new InvalidOperationException("OneFuzzDataStorage did not have resource group name");

    public ResourceIdentifier GetResourceGroupResourceIdentifier() => _config.OneFuzzResourceGroup;

    public string GetInstanceName() => _config.OneFuzzInstanceName;

    public ResourceGroupResource GetResourceGroupResource()
        => ArmClient.GetResourceGroupResource(GetResourceGroupResourceIdentifier());

    public SubscriptionResource GetSubscriptionResource()
        => ArmClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(GetSubscription()));

    public Uri GetInstanceUrl() => _config.OneFuzzEndpoint ?? new($"https://{GetInstanceName()}.azurewebsites.net");

    private static readonly object _baseRegionKey = new(); // we only need equality/hashcode
    public Async.Task<Region> GetBaseRegion() {
        return _cache.GetOrCreateAsync(_baseRegionKey, async _ => {
            var rg = await ArmClient.GetResourceGroupResource(GetResourceGroupResourceIdentifier()).GetAsync();
            if (rg.GetRawResponse().IsError) {
                throw new Exception($"Failed to get base region due to [{rg.GetRawResponse().Status}] {rg.GetRawResponse().ReasonPhrase}");
            }
            return Region.Parse(rg.Value.Data.Location.Name);
        })!; // NULLABLE: only this method inserts _baseRegionKey so it cannot be null
    }

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

        return new($"{resourceGroupPath}/Microsoft.ManagedIdentity/userAssignedIdentities/{scalesetIdName}");
    }

    public GenericResource ParseResourceId(ResourceIdentifier resourceId)
        => ArmClient.GetGenericResource(resourceId);

    public GenericResource ParseResourceId(string resourceId)
        => ArmClient.GetGenericResource(new ResourceIdentifier(resourceId));

    public async Async.Task<GenericResource> GetData(GenericResource resource) {
        if (!resource.HasData) {
            return await resource.GetAsync();
        }
        return resource;
    }

    private static readonly object _regionsKey = new(); // we only need equality/hashcode
    public Task<IReadOnlyList<Region>> GetRegions()
        => _cache.GetOrCreateAsync<IReadOnlyList<Region>>(
            _regionsKey,
            async entry => {
                // cache for one day
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                var subscriptionId = SubscriptionResource.CreateResourceIdentifier(GetSubscription());
                return await ArmClient.GetSubscriptionResource(subscriptionId)
                    .GetLocationsAsync()
                    .Select(x => Region.Parse(x.Name))
                    .ToListAsync();
            })!; // NULLABLE: only this method inserts _regionsKey so it cannot be null
}


sealed class GraphQueryException : Exception {
    public GraphQueryException(string? message) : base(message) {
    }
}
