﻿using Azure.Core;
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

    public string GetBaseRegion();

    public Uri GetInstanceUrl();
}

public class Creds : ICreds {
    private readonly ArmClient _armClient;
    private readonly DefaultAzureCredential _azureCredential;
    private readonly IServiceConfig _config;

    public ArmClient ArmClient => _armClient;

    public Creds(IServiceConfig config) {
        _config = config;
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

    public string GetBaseRegion() {
        return ArmClient.GetResourceGroupResource(GetResourceGroupResourceIdentifier())
            .Get().Value.Data.Location.Name;
    }

    public Uri GetInstanceUrl() {
        return new Uri($"https://{GetInstanceName()}.azurewebsites.net");
    }
}
