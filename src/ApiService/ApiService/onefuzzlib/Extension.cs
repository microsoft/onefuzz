using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IExtensions {
    public Async.Task<IList<VirtualMachineScaleSetExtensionData>> FuzzExtensions(Pool pool, Scaleset scaleset);

    public Async.Task<Dictionary<string, VirtualMachineExtensionData>> ReproExtensions(AzureLocation region, Os reproOs, Guid reproId, ReproConfig reproConfig, Container? setupContainer);
}

public class Extensions : IExtensions {
    IOnefuzzContext _context;

    private static readonly JsonSerializerOptions _extensionSerializerOptions = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Extensions(IOnefuzzContext context) {
        _context = context;
    }

    public async Async.Task<Uri?> ConfigUrl(Container container, string fileName, bool withSas) {
        if (withSas)
            return await _context.Containers.GetFileSasUrl(container, fileName, StorageType.Config, BlobSasPermissions.Read);
        else
            return await _context.Containers.GetFileUrl(container, fileName, StorageType.Config);
    }

    public async Async.Task<IList<VMExtensionWrapper>> GenericExtensions(AzureLocation region, Os vmOs) {
        var extensions = new List<VMExtensionWrapper>();

        var instanceConfig = await _context.ConfigOperations.Fetch();
        extensions.Add(await MonitorExtension(region, vmOs));

        var depenency = DependencyExtension(region, vmOs);
        if (depenency is not null) {
            extensions.Add(depenency);
        }

        if (instanceConfig.Extensions is not null) {

            if (instanceConfig.Extensions.Keyvault is not null) {
                var keyvault = KeyVaultExtension(region, instanceConfig.Extensions.Keyvault, vmOs);
                extensions.Add(keyvault);
            }

            if (instanceConfig.Extensions.Geneva is not null && vmOs == Os.Windows) {
                var geneva = GenevaExtension(region);
                extensions.Add(geneva);
            }

            if (instanceConfig.Extensions.AzureMonitor is not null && vmOs == Os.Linux) {
                var azMon = AzMonExtension(region, instanceConfig.Extensions.AzureMonitor);
                extensions.Add(azMon);
            }

            if (instanceConfig.Extensions.AzureSecurity is not null && vmOs == Os.Linux) {
                var azSec = AzSecExtension(region);
                extensions.Add(azSec);
            }
        }

        return extensions;
    }

    public static VMExtensionWrapper KeyVaultExtension(AzureLocation region, KeyvaultExtensionConfig keyVault, Os vmOs) {
        var keyVaultName = keyVault.KeyVaultName;
        var certName = keyVault.CertName;
        var uri = keyVaultName + certName;

        if (vmOs == Os.Windows) {
            return new VMExtensionWrapper {
                Location = region,
                Name = "KVVMExtensionForWindows",
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForWindows",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(JsonSerializer.Serialize(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreName = "MY",
                        LinkOnRenewal = false,
                        CertificateStoreLocation = "LocalMachine",
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                }, _extensionSerializerOptions))
            };
        } else if (vmOs == Os.Linux) {
            var certPath = keyVault.CertPath;
            var extensionStore = keyVault.ExtensionStore;
            var certLocation = certPath + extensionStore;

            return new VMExtensionWrapper {
                Location = region,
                Name = "KVVMExtensionForLinux",
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForLinux",
                TypeHandlerVersion = "2.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(JsonSerializer.Serialize(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreLocation = certLocation,
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                }, _extensionSerializerOptions))
            };
        } else {
            throw new NotSupportedException($"unsupported os {vmOs}");
        }
    }

    public static VMExtensionWrapper AzSecExtension(AzureLocation region) {
        return new VMExtensionWrapper {
            Location = region,
            Name = "AzureSecurityLinuxAgent",
            Publisher = "Microsoft.Azure.Security.Monitoring",
            TypePropertiesType = "AzureSecurityLinuxAgent",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            Settings = new BinaryData(JsonSerializer.Serialize(new { EnableGenevaUpload = true, EnableAutoConfig = true }, _extensionSerializerOptions))
        };

    }

    public static VMExtensionWrapper AzMonExtension(AzureLocation region, AzureMonitorExtensionConfig azureMonitor) {
        var authId = azureMonitor.MonitoringGCSAuthId;
        var configVersion = azureMonitor.ConfigVersion;
        var moniker = azureMonitor.Moniker;
        var namespaceName = azureMonitor.Namespace;
        var environment = azureMonitor.MonitoringGSEnvironment;
        var account = azureMonitor.MonitoringGCSAccount;
        var authIdType = azureMonitor.MonitoringGCSAuthIdType;

        return new VMExtensionWrapper {
            Location = region,
            Name = "AzureMonitorLinuxAgent",
            Publisher = "Microsoft.Azure.Monitor",
            TypePropertiesType = "AzureMonitorLinuxAgent",
            AutoUpgradeMinorVersion = true,
            TypeHandlerVersion = "1.0",
            Settings = new BinaryData(JsonSerializer.Serialize(new { GCS_AUTO_CONFIG = true }, _extensionSerializerOptions)),
            ProtectedSettings =
                new BinaryData(JsonSerializer.Serialize(
                    new {
                        ConfigVersion = configVersion,
                        Moniker = moniker,
                        Namespace = namespaceName,
                        MonitoringGCSEnvironment = environment,
                        MonitoringGCSAccount = account,
                        MonitoringGCSRegion = region,
                        MonitoringGCSAuthId = authId,
                        MonitoringGCSAuthIdType = authIdType,
                    }, _extensionSerializerOptions))
        };
    }

    public static VMExtensionWrapper GenevaExtension(AzureLocation region) {
        return new VMExtensionWrapper {
            Location = region,
            Name = "Microsoft.Azure.Geneva.GenevaMonitoring",
            Publisher = "Microsoft.Azure.Geneva",
            TypePropertiesType = "GenevaMonitoring",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            EnableAutomaticUpgrade = true,
        };
    }

    public static VMExtensionWrapper? DependencyExtension(AzureLocation region, Os vmOs) {

        if (vmOs == Os.Windows) {
            return new VMExtensionWrapper {
                Location = region,
                Name = "DependencyAgentWindows",
                AutoUpgradeMinorVersion = true,
                Publisher = "Microsoft.Azure.Monitoring.DependencyAgent",
                TypePropertiesType = "DependencyAgentWindows",
                TypeHandlerVersion = "9.5"
            };
        } else {
            // THIS TODO IS FROM PYTHON CODE
            //# TODO: dependency agent for linux is not reliable
            //# extension = {
            //#     "name": "DependencyAgentLinux",
            //#     "publisher": "Microsoft.Azure.Monitoring.DependencyAgent",
            //#     "type": "DependencyAgentLinux",
            //#     "typeHandlerVersion": "9.5",
            //#     "location": vm.region,
            //#     "autoUpgradeMinorVersion": True,
            //# }
            return null;
        }
    }


    public async Async.Task<Uri?> BuildPoolConfig(Pool pool) {
        var instanceId = await _context.Containers.GetInstanceId();

        var queueSas = await _context.Queue.GetQueueSas("node-heartbeat", StorageType.Config, QueueSasPermissions.Add);
        var config = new AgentConfig(
            ClientCredentials: null,
            OneFuzzUrl: _context.Creds.GetInstanceUrl(),
            PoolName: pool.Name,
            HeartbeatQueue: queueSas,
            InstanceTelemetryKey: _context.ServiceConfiguration.ApplicationInsightsInstrumentationKey,
            MicrosoftTelemetryKey: _context.ServiceConfiguration.OneFuzzTelemetry,
            MultiTenantDomain: _context.ServiceConfiguration.MultiTenantDomain,
            InstanceId: instanceId
            );

        var fileName = $"{pool.Name}/config.json";
        await _context.Containers.SaveBlob(new Container("vm-scripts"), fileName, (JsonSerializer.Serialize(config, EntityConverter.GetJsonSerializerOptions())), StorageType.Config);
        return await ConfigUrl(new Container("vm-scripts"), fileName, false);
    }


    public async Async.Task<Uri?> BuildScaleSetScript(Pool pool, Scaleset scaleSet) {
        List<string> commands = new();
        var extension = pool.Os == Os.Windows ? "ps1" : "sh";
        var fileName = $"{scaleSet.ScalesetId}/scaleset-setup.{extension}";
        var sep = pool.Os == Os.Windows ? "\r\n" : "\n";

        if (pool.Os == Os.Windows && scaleSet.Auth is not null) {
            var sshKey = scaleSet.Auth.PublicKey.Trim();
            var sshPath = "$env:ProgramData/ssh/administrators_authorized_keys";
            commands.Add($"Set-Content -Path {sshPath} -Value \"{sshKey}\"");
        }

        await _context.Containers.SaveBlob(new Container("vm-scripts"), fileName, string.Join(sep, commands) + sep, StorageType.Config);
        return await _context.Containers.GetFileUrl(new Container("vm-scripts"), fileName, StorageType.Config);
    }

    public async Async.Task UpdateManagedScripts() {
        var instanceSpecificSetupSas = await _context.Containers.GetContainerSasUrl(new Container("instance-specific-setup"), StorageType.Config, BlobContainerSasPermissions.List | BlobContainerSasPermissions.Read);
        var toolsSas = await _context.Containers.GetContainerSasUrl(new Container("tools"), StorageType.Config, BlobContainerSasPermissions.List | BlobContainerSasPermissions.Read);

        string[] commands = {
            $"azcopy sync '{instanceSpecificSetupSas}' instance-specific-setup",
            $"azcopy sync '{toolsSas}' tools"
        };

        await _context.Containers.SaveBlob(new Container("vm-scripts"), "managed.ps1", string.Join("\r\n", commands) + "\r\n", StorageType.Config);
        await _context.Containers.SaveBlob(new Container("vm-scripts"), "managed.sh", string.Join("\n", commands) + "\n", StorageType.Config);
    }

    public async Async.Task<VMExtensionWrapper> AgentConfig(AzureLocation region, Os vmOs, AgentMode mode, List<Uri>? urls = null, bool withSas = false) {
        await UpdateManagedScripts();
        var urlsUpdated = urls ?? new();

        if (vmOs == Os.Windows) {
            var vmScripts = await ConfigUrl(new Container("vm-scripts"), "managed.ps1", withSas) ?? throw new Exception("failed to get VmScripts config url");
            var toolsAzCopy = await ConfigUrl(new Container("tools"), "win64/azcopy.exe", withSas) ?? throw new Exception("failed to get toolsAzCopy config url");
            var toolsSetup = await ConfigUrl(new Container("tools"), "win64/setup.ps1", withSas) ?? throw new Exception("failed to get toolsSetup config url");
            var toolsOneFuzz = await ConfigUrl(new Container("tools"), "win64/onefuzz.ps1", withSas) ?? throw new Exception("failed to get toolsOneFuzz config url");

            urlsUpdated.Add(vmScripts);
            urlsUpdated.Add(toolsAzCopy);
            urlsUpdated.Add(toolsSetup);
            urlsUpdated.Add(toolsOneFuzz);

            var toExecuteCmd = $"powershell -ExecutionPolicy Unrestricted -File win64/setup.ps1 -mode {mode.ToString().ToLowerInvariant()}";

            var extension = new VMExtensionWrapper {
                Name = "CustomScriptExtension",
                TypePropertiesType = "CustomScriptExtension",
                Publisher = "Microsoft.Compute",
                Location = region,
                ForceUpdateTag = Guid.NewGuid().ToString(),
                TypeHandlerVersion = "1.9",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(JsonSerializer.Serialize(new { commandToExecute = toExecuteCmd, fileUris = urlsUpdated }, _extensionSerializerOptions)),
                ProtectedSettings = new BinaryData(JsonSerializer.Serialize(new { managedIdentity = new Dictionary<string, string>() }, _extensionSerializerOptions))
            };
            return extension;
        } else if (vmOs == Os.Linux) {

            var vmScripts = await ConfigUrl(new Container("vm-scripts"), "managed.sh", withSas) ?? throw new Exception("failed to get VmScripts config url");
            var toolsAzCopy = await ConfigUrl(new Container("tools"), "linux/azcopy", withSas) ?? throw new Exception("failed to get toolsAzCopy config url");
            var toolsSetup = await ConfigUrl(new Container("tools"), "linux/setup.sh", withSas) ?? throw new Exception("failed to get toolsSetup config url");

            urlsUpdated.Add(vmScripts);
            urlsUpdated.Add(toolsAzCopy);
            urlsUpdated.Add(toolsSetup);

            var toExecuteCmd = $"sh setup.sh {mode.ToString().ToLowerInvariant()}";
            var extensionSettings = JsonSerializer.Serialize(new { CommandToExecute = toExecuteCmd, FileUris = urlsUpdated }, _extensionSerializerOptions);
            var protectedExtensionSettings = JsonSerializer.Serialize(new { ManagedIdentity = new Dictionary<string, string>() }, _extensionSerializerOptions);

            var extension = new VMExtensionWrapper {
                Name = "CustomScript",
                Publisher = "Microsoft.Azure.Extensions",
                TypePropertiesType = "CustomScript",
                TypeHandlerVersion = "2.1",
                Location = region,
                ForceUpdateTag = Guid.NewGuid().ToString(),
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(extensionSettings),
                ProtectedSettings = new BinaryData(protectedExtensionSettings)
            };
            return extension;
        }

        throw new NotSupportedException($"unsupported OS: {vmOs}");
    }

    public async Async.Task<VMExtensionWrapper> MonitorExtension(AzureLocation region, Os vmOs) {
        var settings = await _context.LogAnalytics.GetMonitorSettings();
        var extensionSettings = JsonSerializer.Serialize(new { WorkspaceId = settings.Id }, _extensionSerializerOptions);
        var protectedExtensionSettings = JsonSerializer.Serialize(new { WorkspaceKey = settings.Key }, _extensionSerializerOptions);
        if (vmOs == Os.Windows) {
            return new VMExtensionWrapper {
                Location = region,
                Name = "OMSExtension",
                TypePropertiesType = "MicrosoftMonitoringAgent",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(extensionSettings),
                ProtectedSettings = new BinaryData(protectedExtensionSettings)
            };
        } else if (vmOs == Os.Linux) {
            return new VMExtensionWrapper {
                Location = region,
                Name = "OMSExtension",
                TypePropertiesType = "OmsAgentForLinux",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.12",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(extensionSettings),
                ProtectedSettings = new BinaryData(protectedExtensionSettings)
            };
        } else {
            throw new NotSupportedException($"unsupported os: {vmOs}");
        }
    }


    public async Async.Task<IList<VirtualMachineScaleSetExtensionData>> FuzzExtensions(Pool pool, Scaleset scaleset) {
        var poolConfig = await BuildPoolConfig(pool) ?? throw new Exception("pool config url is null");
        var scaleSetScript = await BuildScaleSetScript(pool, scaleset) ?? throw new Exception("scaleSet script url is null");
        var urls = new List<Uri>() { poolConfig, scaleSetScript };

        var fuzzExtension = await AgentConfig(scaleset.Region, pool.Os, AgentMode.Fuzz, urls);
        var extensions = await GenericExtensions(scaleset.Region, pool.Os);

        extensions.Add(fuzzExtension);
        return extensions.Select(extension => extension.GetAsVirtualMachineScaleSetExtension()).ToList();
    }

    public async Task<Dictionary<string, VirtualMachineExtensionData>> ReproExtensions(AzureLocation region, Os reproOs, Guid reproId, ReproConfig reproConfig, Container? setupContainer) {
        // TODO: what about contents of repro.ps1 / repro.sh?
        var report = await _context.Reports.GetReport(reproConfig.Container, reproConfig.Path);
        report.EnsureNotNull($"invalid report: {reproConfig}");
        report?.InputBlob.EnsureNotNull("unable to perform reproduction without an input blob");

        var commands = new List<string>();
        if (setupContainer != null) {
            var containerSasUrl = await _context.Containers.GetContainerSasUrl(
                setupContainer,
                StorageType.Corpus,
                BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List
            );
            commands.Add(
                $"azcopy sync '{containerSasUrl}' ./setup"
            );
        }

        var urls = new List<Uri>()
        {
            await _context.Containers.GetFileSasUrl(
                reproConfig.Container,
                reproConfig.Path,
                StorageType.Corpus,
                BlobSasPermissions.Read
            ),
            await _context.Containers.GetFileSasUrl(
                report?.InputBlob?.container!,
                report?.InputBlob?.Name!,
                StorageType.Corpus,
                BlobSasPermissions.Read
            )
        };

        List<string> reproFiles;
        string taskScript;
        string scriptName;
        if (reproOs == Os.Windows) {
            reproFiles = new List<string>()
            {
                $"{reproId}/repro.ps1"
            };
            taskScript = string.Join("\r\n", commands);
            scriptName = "task-setup.ps1";
        } else {
            reproFiles = new List<string>()
            {
                $"{reproId}/repro.sh",
                $"{reproId}/repro-stdout.sh"
            };
            commands.Add("chmod -R +x setup");
            taskScript = string.Join("\n", commands);
            scriptName = "task-setup.sh";
        }

        await _context.Containers.SaveBlob(
            new Container("task-configs"),
            $"{reproId}/{scriptName}",
            taskScript,
            StorageType.Config
        );

        foreach (var reproFile in reproFiles) {
            urls.AddRange(new List<Uri>()
            {
                await _context.Containers.GetFileSasUrl(
                    new Container("repro-scripts"),
                    reproFile,
                    StorageType.Config,
                    BlobSasPermissions.Read
                ),
                await _context.Containers.GetFileSasUrl(
                    new Container("task-configs"),
                    $"{reproId}/{scriptName}",
                    StorageType.Config,
                    BlobSasPermissions.Read
                )
            });
        }

        var baseExtension = await AgentConfig(region, reproOs, AgentMode.Repro, urls: urls, withSas: true);
        var extensions = await GenericExtensions(region, reproOs);
        extensions.Add(baseExtension);

        var extensionsDict = new Dictionary<string, VirtualMachineExtensionData>();
        foreach (var extension in extensions) {
            var (name, data) = extension.GetAsVirtualMachineExtension();
            extensionsDict.Add(name, data);
        }

        return extensionsDict;
    }

}
