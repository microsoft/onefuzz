using Azure.Identity;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace Microsoft.OneFuzz.Service;

public interface ICreds
{
    public DefaultAzureCredential GetIdentity();

    public string GetSubcription();

    public string GetBaseResourceGroup();

    public ResourceIdentifier GetResourceGroupResourceIdentifier();


    public ArmClient ArmClient { get; }

    public ResourceGroupResource GetResourceGroupResource();
}

public class Creds : ICreds
{
    private readonly Lazy<ArmClient> _armClient;

    public ArmClient ArmClient => _armClient.Value;

    public Creds()
    {
        _armClient = new Lazy<ArmClient>(() => new ArmClient(this.GetIdentity(), this.GetSubcription()), true);
    }

    // TODO: @cached
    public DefaultAzureCredential GetIdentity()
    {
        // TODO: AllowMoreWorkers
        // TODO: ReduceLogging
        return new DefaultAzureCredential();
    }

    public string GetSubcription()
    {
        var storageResourceId = EnvironmentVariables.OneFuzz.DataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        var storageResource = new ResourceIdentifier(storageResourceId);
        return storageResource.SubscriptionId!;
    }

    public string GetBaseResourceGroup()
    {
        var storageResourceId = EnvironmentVariables.OneFuzz.DataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        var storageResource = new ResourceIdentifier(storageResourceId);
        return storageResource.ResourceGroupName!;
    }

    public ResourceIdentifier GetResourceGroupResourceIdentifier()
    {
        var resourceId = EnvironmentVariables.OneFuzz.ResourceGroup
            ?? throw new System.Exception("Resource group env var is not present");
        return new ResourceIdentifier(resourceId);
    }

    public ResourceGroupResource GetResourceGroupResource()
    {
        var resourceId = GetResourceGroupResourceIdentifier();
        return ArmClient.GetResourceGroupResource(resourceId);
    }
}
