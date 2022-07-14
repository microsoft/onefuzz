using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IExtensions {
    public Async.Task<IList<VirtualMachineScaleSetExtensionData>> FuzzExtensions(Pool pool, Scaleset scaleset);

    public Async.Task<IList<VirtualMachineExtensionData>> ReproExtensions(AzureLocation region, Os reproOs, Guid reproId, ReproConfig reproConfig, Container? setupContainer);
}

public class Extensions : IExtensions {
    IServiceConfig _serviceConfig;
    ICreds _creds;
    IQueue _queue;
    IContainers _containers;
    IConfigOperations _instanceConfigOps;
    ILogAnalytics _logAnalytics;

    IOnefuzzContext _context;

    public Extensions(IServiceConfig config, ICreds creds, IQueue queue, IContainers containers, IConfigOperations instanceConfigOps, ILogAnalytics logAnalytics, IOnefuzzContext context) {
        _serviceConfig = config;
        _creds = creds;
        _queue = queue;
        _containers = containers;
        _instanceConfigOps = instanceConfigOps;
        _logAnalytics = logAnalytics;
        _context = context;
    }

    public async Async.Task<Uri?> ConfigUrl(Container container, string fileName, bool withSas) {
        if (withSas)
            return await _containers.GetFileSasUrl(container, fileName, StorageType.Config, BlobSasPermissions.Read);
        else
            return await _containers.GetFileUrl(container, fileName, StorageType.Config);
    }

    public async Async.Task<IList<VirtualMachineScaleSetExtensionData>> GenericExtensions(string region, Os vmOs) {
        var extensions = new List<VirtualMachineScaleSetExtensionData>();

        var instanceConfig = await _instanceConfigOps.Fetch();
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

    public async Async.Task<IList<VirtualMachineExtensionData>> GenericExtensions(AzureLocation region, Os vmOs) {
        var extensions = new List<VirtualMachineExtensionData>();

        var instanceConfig = await _instanceConfigOps.Fetch();
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

    public static VirtualMachineScaleSetExtensionData KeyVaultExtension(string region, KeyvaultExtensionConfig keyVault, Os vmOs) {
        var keyVaultName = keyVault.KeyVaultName;
        var certName = keyVault.CertName;
        var uri = keyVaultName + certName;

        if (vmOs == Os.Windows) {
            return new VirtualMachineScaleSetExtensionData {
                Name = "KVVMExtensionForWindows",
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForWindows",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreName = "MY",
                        LinkOnRenewal = false,
                        CertificateStoreLocation = "LocalMachine",
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                })
            };
        } else if (vmOs == Os.Linux) {
            var certPath = keyVault.CertPath;
            var extensionStore = keyVault.ExtensionStore;
            var certLocation = certPath + extensionStore;

            return new VirtualMachineScaleSetExtensionData {
                Name = "KVVMExtensionForLinux",
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForLinux",
                TypeHandlerVersion = "2.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreLocation = certLocation,
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                })
            };
        } else {
            throw new NotImplementedException($"unsupported os {vmOs}");
        }
    }

    public static VirtualMachineExtensionData KeyVaultExtension(AzureLocation region, KeyvaultExtensionConfig keyVault, Os vmOs) {
        var keyVaultName = keyVault.KeyVaultName;
        var certName = keyVault.CertName;
        var uri = keyVaultName + certName;

        if (vmOs == Os.Windows) {
            return new VirtualMachineExtensionData(region) {
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForWindows",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreName = "MY",
                        LinkOnRenewal = false,
                        CertificateStoreLocation = "LocalMachine",
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                })
            };
        } else if (vmOs == Os.Linux) {
            var certPath = keyVault.CertPath;
            var extensionStore = keyVault.ExtensionStore;
            var certLocation = certPath + extensionStore;

            return new VirtualMachineExtensionData(region) {
                Publisher = "Microsoft.Azure.KeyVault",
                TypePropertiesType = "KeyVaultForLinux",
                TypeHandlerVersion = "2.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new {
                    SecretsManagementSettings = new {
                        PollingIntervalInS = "3600",
                        CertificateStoreLocation = certLocation,
                        RequireInitialSync = true,
                        ObservedCertificates = new string[] { uri },
                    }
                })
            };
        } else {
            throw new NotImplementedException($"unsupported os {vmOs}");
        }
    }

    public static VirtualMachineScaleSetExtensionData AzSecExtension(string region) {
        return new VirtualMachineScaleSetExtensionData {
            Name = "AzureSecurityLinuxAgent",
            Publisher = "Microsoft.Azure.Security.Monitoring",
            TypePropertiesType = "AzureSecurityLinuxAgent",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            Settings = new BinaryData(new { EnableGenevaUpload = true, EnableAutoConfig = true })
        };

    }

    public static VirtualMachineExtensionData AzSecExtension(AzureLocation region) {
        return new VirtualMachineExtensionData(region) {
            Publisher = "Microsoft.Azure.Security.Monitoring",
            TypePropertiesType = "AzureSecurityLinuxAgent",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            Settings = new BinaryData(new { EnableGenevaUpload = true, EnableAutoConfig = true })
        };

    }

    public static VirtualMachineScaleSetExtensionData AzMonExtension(string region, AzureMonitorExtensionConfig azureMonitor) {
        var authId = azureMonitor.MonitoringGCSAuthId;
        var configVersion = azureMonitor.ConfigVersion;
        var moniker = azureMonitor.Moniker;
        var namespaceName = azureMonitor.Namespace;
        var environment = azureMonitor.MonitoringGSEnvironment;
        var account = azureMonitor.MonitoringGCSAccount;
        var authIdType = azureMonitor.MonitoringGCSAuthIdType;

        return new VirtualMachineScaleSetExtensionData {
            Name = "AzureMonitorLinuxAgent",
            Publisher = "Microsoft.Azure.Monitor",
            TypePropertiesType = "AzureMonitorLinuxAgent",
            AutoUpgradeMinorVersion = true,
            TypeHandlerVersion = "1.0",
            Settings = new BinaryData(new { GCS_AUTO_CONFIG = true }),
            ProtectedSettings =
                new BinaryData(
                    new {
                        ConfigVersion = configVersion,
                        Moniker = moniker,
                        Namespace = namespaceName,
                        MonitoringGCSEnvironment = environment,
                        MonitoringGCSAccount = account,
                        MonitoringGCSRegion = region,
                        MonitoringGCSAuthId = authId,
                        MonitoringGCSAuthIdType = authIdType,
                    })
        };
    }

    public static VirtualMachineExtensionData AzMonExtension(AzureLocation region, AzureMonitorExtensionConfig azureMonitor) {
        var authId = azureMonitor.MonitoringGCSAuthId;
        var configVersion = azureMonitor.ConfigVersion;
        var moniker = azureMonitor.Moniker;
        var namespaceName = azureMonitor.Namespace;
        var environment = azureMonitor.MonitoringGSEnvironment;
        var account = azureMonitor.MonitoringGCSAccount;
        var authIdType = azureMonitor.MonitoringGCSAuthIdType;

        return new VirtualMachineExtensionData(region) {
            Publisher = "Microsoft.Azure.Monitor",
            TypePropertiesType = "AzureMonitorLinuxAgent",
            AutoUpgradeMinorVersion = true,
            TypeHandlerVersion = "1.0",
            Settings = new BinaryData(new { GCS_AUTO_CONFIG = true }),
            ProtectedSettings =
                new BinaryData(
                    new {
                        ConfigVersion = configVersion,
                        Moniker = moniker,
                        Namespace = namespaceName,
                        MonitoringGCSEnvironment = environment,
                        MonitoringGCSAccount = account,
                        MonitoringGCSRegion = region,
                        MonitoringGCSAuthId = authId,
                        MonitoringGCSAuthIdType = authIdType,
                    })
        };
    }



    public static VirtualMachineScaleSetExtensionData GenevaExtension(string region) {
        return new VirtualMachineScaleSetExtensionData {
            Name = "Microsoft.Azure.Geneva.GenevaMonitoring",
            Publisher = "Microsoft.Azure.Geneva",
            TypePropertiesType = "GenevaMonitoring",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            EnableAutomaticUpgrade = true,
        };
    }

    public static VirtualMachineExtensionData GenevaExtension(AzureLocation region) {
        return new VirtualMachineExtensionData(region) {
            Publisher = "Microsoft.Azure.Geneva",
            TypePropertiesType = "GenevaMonitoring",
            TypeHandlerVersion = "2.0",
            AutoUpgradeMinorVersion = true,
            EnableAutomaticUpgrade = true,
        };
    }

    public static VirtualMachineScaleSetExtensionData? DependencyExtension(string region, Os vmOs) {

        if (vmOs == Os.Windows) {
            return new VirtualMachineScaleSetExtensionData {
                AutoUpgradeMinorVersion = true,
                Name = "DependencyAgentWindows",
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

    public static VirtualMachineExtensionData? DependencyExtension(AzureLocation region, Os vmOs) {

        if (vmOs == Os.Windows) {
            return new VirtualMachineExtensionData(region) {
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
        var instanceId = await _containers.GetInstanceId();

        var queueSas = await _queue.GetQueueSas("node-heartbeat", StorageType.Config, QueueSasPermissions.Add);
        var config = new AgentConfig(
            ClientCredentials: null,
            OneFuzzUrl: _creds.GetInstanceUrl(),
            PoolName: pool.Name,
            HeartbeatQueue: queueSas,
            InstanceTelemetryKey: _serviceConfig.ApplicationInsightsInstrumentationKey,
            MicrosoftTelemetryKey: _serviceConfig.OneFuzzTelemetry,
            MultiTenantDomain: _serviceConfig.MultiTenantDomain,
            InstanceId: instanceId
            );

        var fileName = $"{pool.Name}/config.json";
        await _containers.SaveBlob(new Container("vm-scripts"), fileName, (JsonSerializer.Serialize(config, EntityConverter.GetJsonSerializerOptions())), StorageType.Config);
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

        await _containers.SaveBlob(new Container("vm-scripts"), fileName, string.Join(sep, commands) + sep, StorageType.Config);
        return await _containers.GetFileUrl(new Container("vm-scripts"), fileName, StorageType.Config);
    }

    public async Async.Task UpdateManagedScripts() {
        var instanceSpecificSetupSas = _containers.GetContainerSasUrl(new Container("instance-specific-setup"), StorageType.Config, BlobContainerSasPermissions.List | BlobContainerSasPermissions.Read);
        var toolsSas = _containers.GetContainerSasUrl(new Container("tools"), StorageType.Config, BlobContainerSasPermissions.List | BlobContainerSasPermissions.Read);

        string[] commands = {
            $"azcopy sync '{instanceSpecificSetupSas}' instance-specific-setup",
            $"azcopy sync '{toolsSas}' tools"
        };

        await _containers.SaveBlob(new Container("vm-scripts"), "managed.ps1", string.Join("\r\n", commands) + "\r\n", StorageType.Config);
        await _containers.SaveBlob(new Container("vm-scripts"), "managed.sh", string.Join("\n", commands) + "\n", StorageType.Config);
    }

    private static string AgentConfigCommandToExecuteWindows(AgentMode mode) {
        return $"powershell -ExecutionPolicy Unrestricted -File win64/setup.ps1 -mode {mode}";
    }

    private static string AgentConfigCommandToExecuteLinux(AgentMode mode) {
        return $"sh setup.sh {mode}";
    }

    private async Task<List<Uri>> AgentConfigFileUrlsWindows(bool withSas = false) {
        var urlsUpdated = new List<Uri>();
        var vmScripts = await ConfigUrl(new Container("vm-scripts"), "managed.ps1", withSas) ?? throw new Exception("failed to get VmScripts config url");
        var toolsAzCopy = await ConfigUrl(new Container("tools"), "win64/azcopy.exe", withSas) ?? throw new Exception("failed to get toolsAzCopy config url");
        var toolsSetup = await ConfigUrl(new Container("tools"), "win64/setup.ps1", withSas) ?? throw new Exception("failed to get toolsSetup config url");
        var toolsOneFuzz = await ConfigUrl(new Container("tools"), "win64/onefuzz.ps1", withSas) ?? throw new Exception("failed to get toolsOneFuzz config url");

        urlsUpdated.Add(vmScripts);
        urlsUpdated.Add(toolsAzCopy);
        urlsUpdated.Add(toolsSetup);
        urlsUpdated.Add(toolsOneFuzz);

        return urlsUpdated;
    }

    private async Task<List<Uri>> AgentConfigFileUrlsLinux(bool withSas = false) {
        var urlsUpdated = new List<Uri>();
        var vmScripts = await ConfigUrl(new Container("vm-scripts"), "managed.sh", withSas) ?? throw new Exception("failed to get VmScripts config url");
        var toolsAzCopy = await ConfigUrl(new Container("tools"), "linux/azcopy", withSas) ?? throw new Exception("failed to get toolsAzCopy config url");
        var toolsSetup = await ConfigUrl(new Container("tools"), "linux/setup.sh", withSas) ?? throw new Exception("failed to get toolsSetup config url");

        urlsUpdated.Add(vmScripts);
        urlsUpdated.Add(toolsAzCopy);
        urlsUpdated.Add(toolsSetup);

        return urlsUpdated;
    }

    public async Async.Task<VirtualMachineScaleSetExtensionData> AgentConfig(string region, Os vmOs, AgentMode mode, List<Uri>? urls = null, bool withSas = false) {
        await UpdateManagedScripts();

        if (vmOs == Os.Windows) {
            var extension = new VirtualMachineScaleSetExtensionData {
                Name = "CustomScriptExtension",
                TypePropertiesType = "CustomScriptExtension",
                Publisher = "Microsoft.Compute",
                ForceUpdateTag = Guid.NewGuid().ToString(),
                TypeHandlerVersion = "1.9",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { commandToExecute = AgentConfigCommandToExecuteWindows(mode), fileUrls = await AgentConfigFileUrlsWindows(withSas) }),
                ProtectedSettings = new BinaryData(new { managedIdentity = new Dictionary<string, string>() })
            };
            return extension;
        } else if (vmOs == Os.Linux) {
            var extension = new VirtualMachineScaleSetExtensionData {
                Name = "CustomScript",
                TypePropertiesType = "CustomScript",
                Publisher = "Microsoft.Azure.Extension",
                ForceUpdateTag = Guid.NewGuid().ToString(),
                TypeHandlerVersion = "2.1",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { CommandToExecute = AgentConfigCommandToExecuteLinux(mode), FileUrls = await AgentConfigFileUrlsLinux(withSas) }),
                ProtectedSettings = new BinaryData(new { ManagedIdentity = new Dictionary<string, string>() })
            };
            return extension;
        }

        throw new NotImplementedException($"unsupported OS: {vmOs}");
    }

    public async Async.Task<VirtualMachineExtensionData> AgentConfig(AzureLocation region, Os vmOs, AgentMode mode, List<Uri>? urls = null, bool withSas = false) {
        await UpdateManagedScripts();

        if (vmOs == Os.Windows) {
            var extension = new VirtualMachineExtensionData(region) {
                TypePropertiesType = "CustomScriptExtension",
                Publisher = "Microsoft.Compute",
                ForceUpdateTag = Guid.NewGuid().ToString(),
                TypeHandlerVersion = "1.9",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { commandToExecute = AgentConfigCommandToExecuteWindows(mode), fileUrls = await AgentConfigFileUrlsWindows(withSas) }),
                ProtectedSettings = new BinaryData(new { managedIdentity = new Dictionary<string, string>() })
            };
            return extension;
        } else if (vmOs == Os.Linux) {
            var extension = new VirtualMachineExtensionData(region) {
                TypePropertiesType = "CustomScript",
                Publisher = "Microsoft.Azure.Extension",
                ForceUpdateTag = Guid.NewGuid().ToString(),
                TypeHandlerVersion = "2.1",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { CommandToExecute = AgentConfigCommandToExecuteLinux(mode), FileUrls = await AgentConfigFileUrlsLinux(withSas) }),
                ProtectedSettings = new BinaryData(new { ManagedIdentity = new Dictionary<string, string>() })
            };
            return extension;
        }

        throw new NotImplementedException($"unsupported OS: {vmOs}");
    }

    public async Async.Task<VirtualMachineScaleSetExtensionData> MonitorExtension(string region, Os vmOs) {
        var settings = await _logAnalytics.GetMonitorSettings();

        if (vmOs == Os.Windows) {
            return new VirtualMachineScaleSetExtensionData {
                Name = "OMSExtension",
                TypePropertiesType = "MicrosoftMonitoringAgent",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { WorkSpaceId = settings.Id }),
                ProtectedSettings = new BinaryData(new { WorkspaceKey = settings.Key })
            };
        } else if (vmOs == Os.Linux) {
            return new VirtualMachineScaleSetExtensionData {
                Name = "OMSExtension",
                TypePropertiesType = "OmsAgentForLinux",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.12",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { WorkSpaceId = settings.Id }),
                ProtectedSettings = new BinaryData(new { WorkspaceKey = settings.Key })
            };
        } else {
            throw new NotImplementedException($"unsupported os: {vmOs}");
        }
    }

    public async Async.Task<VirtualMachineExtensionData> MonitorExtension(AzureLocation region, Os vmOs) {
        var settings = await _logAnalytics.GetMonitorSettings();

        if (vmOs == Os.Windows) {
            return new VirtualMachineExtensionData(region) {
                TypePropertiesType = "MicrosoftMonitoringAgent",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.0",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { WorkSpaceId = settings.Id }),
                ProtectedSettings = new BinaryData(new { WorkspaceKey = settings.Key })
            };
        } else if (vmOs == Os.Linux) {
            return new VirtualMachineExtensionData(region) {
                TypePropertiesType = "OmsAgentForLinux",
                Publisher = "Microsoft.EnterpriseCloud.Monitoring",
                TypeHandlerVersion = "1.12",
                AutoUpgradeMinorVersion = true,
                Settings = new BinaryData(new { WorkSpaceId = settings.Id }),
                ProtectedSettings = new BinaryData(new { WorkspaceKey = settings.Key })
            };
        } else {
            throw new NotImplementedException($"unsupported os: {vmOs}");
        }
    }


    public async Async.Task<IList<VirtualMachineScaleSetExtensionData>> FuzzExtensions(Pool pool, Scaleset scaleset) {
        var poolConfig = await BuildPoolConfig(pool) ?? throw new Exception("pool config url is null");
        var scaleSetScript = await BuildScaleSetScript(pool, scaleset) ?? throw new Exception("scaleSet script url is null");
        var urls = new List<Uri>() { poolConfig, scaleSetScript };

        var fuzzExtension = await AgentConfig(scaleset.Region, pool.Os, AgentMode.Fuzz, urls);
        var extensions = await GenericExtensions(scaleset.Region, pool.Os);

        extensions.Add(fuzzExtension);
        return extensions;
    }

    public async Task<IList<VirtualMachineExtensionData>> ReproExtensions(AzureLocation region, Os reproOs, Guid reproId, ReproConfig reproConfig, Container? setupContainer) {
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
            (await _context.Containers.GetFileSasUrl(
                reproConfig.Container,
                reproConfig.Path,
                StorageType.Corpus,
                BlobSasPermissions.Read
            )),
            (await _context.Containers.GetFileSasUrl(
                report?.InputBlob?.container!,
                report?.InputBlob?.Name!,
                StorageType.Corpus,
                BlobSasPermissions.Read
            ))
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
                (await _context.Containers.GetFileSasUrl(
                    new Container("repro-scripts"),
                    reproFile,
                    StorageType.Config,
                    BlobSasPermissions.Read
                )),
                (await _context.Containers.GetFileSasUrl(
                    new Container("task-configs"),
                    $"{reproId}/{scriptName}",
                    StorageType.Config,
                    BlobSasPermissions.Read
                ))
            });
        }

        var baseExtension = await AgentConfig(region, reproOs, AgentMode.Repro, urls: urls, withSas: true);
        var extensions = await GenericExtensions(region, reproOs);
        extensions.Add(baseExtension);
        return extensions;
    }

}
