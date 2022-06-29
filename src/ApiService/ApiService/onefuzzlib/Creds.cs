using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

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
}

public class Creds : ICreds {
    private readonly ArmClient _armClient;
    private readonly DefaultAzureCredential _azureCredential;
    private readonly IServiceConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArmClient ArmClient => _armClient;

    public Creds(IServiceConfig config, IHttpClientFactory httpClientFactory) {
        _config = config;
        _httpClientFactory = httpClientFactory;
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

    public async Async.Task<string> GetBaseRegion() {
        var rg = await ArmClient.GetResourceGroupResource(GetResourceGroupResourceIdentifier()).GetAsync();
        if (rg.GetRawResponse().IsError) {
            throw new Exception($"Failed to get base region due to [{rg.GetRawResponse().Status}] {rg.GetRawResponse().ReasonPhrase}");
        }
        return rg.Value.Data.Location.Name;
    }

    public Uri GetInstanceUrl() {
        return new Uri($"https://{GetInstanceName()}.azurewebsites.net");
    }

    public record ScaleSetIdentity(string principalId);

    public async Async.Task<Guid> GetScalesetPrincipalId() {
        var path = GetScalesetIdentityResourcePath();
        var uid = ArmClient.GetGenericResource(new ResourceIdentifier(path));

        var resource = await uid.GetAsync();
        var principalId = resource.Value.Data.Properties.ToObjectFromJson<ScaleSetIdentity>().principalId;
        return new Guid(principalId);
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
}

class GraphQueryException : Exception {
    public GraphQueryException(string? message) : base(message) {
    }
}
