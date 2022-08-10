using System.Net.Http;
using System.Net.Http.Json;
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

    public Async.Task<string> GetBaseRegion();

    public Uri GetInstanceUrl();
    public Async.Task<Guid> GetScalesetPrincipalId();
    public Async.Task<T> QueryMicrosoftGraph<T>(HttpMethod method, string resource);

    public GenericResource ParseResourceId(string resourceId);

    public Async.Task<GenericResource> GetData(GenericResource resource);
    Async.Task<IReadOnlyList<string>> GetRegions();
}

public sealed class Creds : ICreds, IDisposable {
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
        var storageResource = new ResourceIdentifier(storageResourceId);
        return storageResource.SubscriptionId!;
    }

    public string GetBaseResourceGroup() {
        var storageResourceId = _config.OneFuzzDataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        var storageResource = new ResourceIdentifier(storageResourceId);
        return storageResource.ResourceGroupName!;
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
            var uid = ArmClient.GetGenericResource(new ResourceIdentifier(path));

            var resource = await uid.GetAsync();
            var principalId = resource.Value.Data.Properties.ToObjectFromJson<ScaleSetIdentity>().principalId;
            return new Guid(principalId);
        });
    }

    public string GetScalesetIdentityResourcePath() {
        var scalesetIdName = $"{GetInstanceName()}-scalesetid";
        var resourceGroupPath = $"/subscriptions/{GetSubscription()}/resourceGroups/{GetBaseResourceGroup()}/providers";

        return $"{resourceGroupPath}/Microsoft.ManagedIdentity/userAssignedIdentities/{scalesetIdName}";
    }


    // https://docs.microsoft.com/en-us/graph/api/overview?view=graph-rest-1.0
    private static readonly Uri _graphResource = new("https://graph.microsoft.com");
    private static readonly Uri _graphResourceEndpoint = new("https://graph.microsoft.com/v1.0");


    public async Task<T> QueryMicrosoftGraph<T>(HttpMethod method, string resource) {
        var cred = GetIdentity();

        var scopes = new string[] { $"{_graphResource}/.default" };
        var accessToken = await cred.GetTokenAsync(new TokenRequestContext(scopes));

        var uri = new Uri($"{_graphResourceEndpoint}/{resource}");
        using var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(new HttpRequestMessage {
            Headers = {
                {"Authorization", $"Bearer {accessToken.Token}"},
                {"Content-Type", "application/json"},
            },
            Method = method,
            RequestUri = uri,
        });

        if (response.IsSuccessStatusCode) {
            var result = await response.Content.ReadFromJsonAsync<T>();
            if (result is null) {
                throw new GraphQueryException($"invalid data expected a json object: HTTP {response.StatusCode}");
            }

            return result;
        } else {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new GraphQueryException($"request did not succeed: HTTP {response.StatusCode} - {errorText}");
        }
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

    public void Dispose() {
        throw new NotImplementedException();
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
