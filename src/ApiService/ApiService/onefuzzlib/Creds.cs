using Azure.Identity;
using Azure.Core;

namespace Microsoft.OneFuzz.Service;

public interface ICreds
{
    public DefaultAzureCredential GetIdentity();

    public string GetSubcription();

    public string GetBaseResourceGroup();

    public ResourceIdentifier GetResourceGroupResourceIdentifier();
}

public class Creds : ICreds
{

    // TODO: @cached
    public DefaultAzureCredential GetIdentity()
    {
        // TODO: AllowMoreWorkers
        // TODO: ReduceLogging
        return new DefaultAzureCredential();
    }

    // TODO: @cached
    public string GetSubcription()
    {
        var storageResourceId = EnvironmentVariables.OneFuzz.DataStorage
            ?? throw new System.Exception("Data storage env var is not present");
        var storageResource = new ResourceIdentifier(storageResourceId);
        return storageResource.SubscriptionId!;
    }

    // TODO: @cached
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
}
