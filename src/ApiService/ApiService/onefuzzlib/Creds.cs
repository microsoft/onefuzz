using Azure.Identity;
using Azure.Core;
using System;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace Microsoft.OneFuzz.Service;

public interface ICreds
{
    public DefaultAzureCredential GetIdentity();

    public string GetSubcription();

    public string GetBaseResourceGroup();

    public ResourceIdentifier GetResourceGroupResourceIdentifier();

    public string GetInstanceName();

    public Async.Task<Guid> GetInstanceId();

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

    private IContainers _containers;

    public Creds(IContainers containers)
    {
        _containers = containers;
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

    public string GetInstanceName()
    {
        var instanceName = EnvironmentVariables.OneFuzz.InstanceName
            ?? throw new System.Exception("Instance Name env var is not present");

        return instanceName;
    }

    public async Async.Task<Guid> GetInstanceId()
    {
        var blob = await _containers.GetBlob(new Container("base-config"), "instance_id", StorageType.Config);
        if (blob == null)
        {
            throw new System.Exception("Blob Not Found");
        }
        return System.Guid.Parse(System.Text.Encoding.Default.GetString(blob.ToArray()));
        
    public ResourceGroupResource GetResourceGroupResource()
    {
        var resourceId = GetResourceGroupResourceIdentifier();
        return ArmClient.GetResourceGroupResource(resourceId);
    }
}
