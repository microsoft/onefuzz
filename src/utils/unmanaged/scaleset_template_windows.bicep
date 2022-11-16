param scaleset_name string
param location string
param networkSecurityGroups_name string
param adminUsername string = 'onefuzz'
param capacity int = 1
param vmSize string = 'Standard_D2s_v3'
param tier string = 'Standard'
param storageAccountName string

@secure()
param adminPassword string

resource storageAccount 'Microsoft.Storage/storageAccounts@2019-06-01' existing = {
  name: 'storageAccountName'
}

var StorageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

var fileUris = [
  // 'https://${storageAccountName}.blob.${environment().suffixes.storage}/vm-scripts/managed.ps1'
  // 'https://${storageAccountName}.blob.${environment().suffixes.storage}/vm-scripts/managed.ps1'
  'https://${storageAccountName}.blob.${environment().suffixes.storage}/tools/config.json'
  'https://${storageAccountName}.blob.${environment().suffixes.storage}/tools/win64/azcopy.exe'
  'https://${storageAccountName}.blob.${environment().suffixes.storage}/tools/win64/setup.ps1'
  'https://${storageAccountName}.blob.${environment().suffixes.storage}/tools/win64/onefuzz.ps1'
]

resource scaleset 'Microsoft.Compute/virtualMachineScaleSets@2022-03-01' = {
  name: scaleset_name
  location: location
  tags: {
    azsecpack: 'nonprod'
    'platformsettings.host_environment.service.platform_optedin_for_rootcerts': 'true'
  }
  sku: {
    name: vmSize
    tier: tier
    capacity: capacity
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    singlePlacementGroup: false
    orchestrationMode: 'Uniform'
    upgradePolicy: {
      mode: 'Manual'
    }
    virtualMachineProfile: {
      osProfile: {
        computerNamePrefix: 'node'
        adminUsername: adminUsername
        adminPassword: adminPassword
        windowsConfiguration: {
          provisionVMAgent: true
          enableAutomaticUpdates: true
        }
        secrets: []
        allowExtensionOperations: true
      }
      storageProfile: {
        osDisk: {
          osType: 'Windows'
          createOption: 'FromImage'
          caching: 'None'
          managedDisk: {
            storageAccountType: 'Premium_LRS'
          }
          diskSizeGB: 127
        }
        imageReference: {
          publisher: 'MicrosoftWindowsDesktop'
          offer: 'Windows-10'
          sku: 'win10-21h2-pro'
          version: 'latest'
        }
      }
      networkProfile: {
        networkInterfaceConfigurations: [
          {
            name: 'onefuzz-nic'
            properties: {
              primary: true
              enableAcceleratedNetworking: false
              dnsSettings: {
                dnsServers: []
              }
              enableIPForwarding: false
              ipConfigurations: [
                {
                  name: 'onefuzz-ip-config'
                  properties: {
                    subnet: {
                      id: subnet.id
                    }
                    privateIPAddressVersion: 'IPv4'
                  }
                }
              ]
            }
          }
        ]
      }
      extensionProfile: {
        extensions: [
          // {
          //   name: 'OMSExtension'
          //   properties: {
          //     autoUpgradeMinorVersion: true
          //     enableAutomaticUpgrade: false
          //     publisher: 'Microsoft.EnterpriseCloud.Monitoring'
          //     type: 'MicrosoftMonitoringAgent'
          //     typeHandlerVersion: '1.0'
          //     settings: {
          //       workspaceId: '77da01e8-838a-41d9-a6d0-c8b180f68904'
          //     }
          //   }
          // }
          // {
          //   name: 'DependencyAgentWindows'
          //   properties: {
          //     autoUpgradeMinorVersion: true
          //     publisher: 'Microsoft.Azure.Monitoring.DependencyAgent'
          //     type: 'DependencyAgentWindows'
          //     typeHandlerVersion: '9.5'
          //     settings: {
          //     }
          //   }
          // }
          // {
          //   name: 'Microsoft.Azure.Geneva.GenevaMonitoring'
          //   properties: {
          //     autoUpgradeMinorVersion: true
          //     enableAutomaticUpgrade: true
          //     publisher: 'Microsoft.Azure.Geneva'
          //     type: 'GenevaMonitoring'
          //     typeHandlerVersion: '2.0'
          //     settings: {
          //     }
          //   }
          // }
          {
            name: 'CustomScriptExtension'
            properties: {
              autoUpgradeMinorVersion: true
              publisher: 'Microsoft.Compute'
              type: 'CustomScriptExtension'
              typeHandlerVersion: '1.10'
              settings: {
                commandToExecute: 'powershell -ExecutionPolicy Unrestricted -File win64/setup.ps1 -mode fuzz'
                fileUris: fileUris
              }
            }
          }
          // {
          //   name: 'Microsoft.Azure.Security.AntimalwareSignature.AntimalwareConfiguration'
          //   properties: {
          //     autoUpgradeMinorVersion: true
          //     enableAutomaticUpgrade: true
          //     publisher: 'Microsoft.Azure.Security.AntimalwareSignature'
          //     type: 'AntimalwareConfiguration'
          //     typeHandlerVersion: '2.0'
          //     settings: {
          //     }
          //   }
          // }
        ]
      }
      priority: 'Regular'
    }
    overprovision: false
    doNotRunExtensionsOnOverprovisionedVMs: false
  }
}
// try to make role assignments to deploy as late as possible in order to have principalId ready
resource readBlobUserAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid('${resourceGroup().id}-user_managed_idenity_read_blob')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${StorageBlobDataReader}'
    principalId: scaleset.identity.principalId
    // reference(scalesetIdentity.id, scalesetIdentity.apiVersion, 'Full').properties.principalId
  }
  dependsOn: [
    storageAccount
  ]
}


// resource scaleset_CustomScriptExtension 'Microsoft.Compute/virtualMachineScaleSets/extensions@2022-03-01' = {
//   parent: scaleset
//   name: 'CustomScriptExtension'
//   properties: {
//     autoUpgradeMinorVersion: true
//     forceUpdateTag: 'e2dc833a-58d2-4f96-a462-4e079f5fc1e0'
//     publisher: 'Microsoft.Compute'
//     type: 'CustomScriptExtension'
//     typeHandlerVersion: '1.9'
//     settings: {
//       commandToExecute: 'powershell -ExecutionPolicy Unrestricted -File win64/setup.ps1 -mode fuzz'
//       fileUris: [
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/vm-scripts/pool_windows/config.json'
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/vm-scripts/be10f6a5-4efd-432f-be1a-25cc18e77900/scaleset-setup.ps1'
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/vm-scripts/managed.ps1'
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/tools/win64/azcopy.exe'
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/tools/win64/setup.ps1'
//         // 'https://func7tkgjsiivmq6i.blob.core.windows.net/tools/win64/onefuzz.ps1'
//       ]
//     }
//   }
// }

// resource scaleset_DependencyAgentWindows 'Microsoft.Compute/virtualMachineScaleSets/extensions@2022-03-01' = {
//   parent: scaleset
//   name: 'DependencyAgentWindows'
//   properties: {
//     autoUpgradeMinorVersion: true
//     publisher: 'Microsoft.Azure.Monitoring.DependencyAgent'
//     type: 'DependencyAgentWindows'
//     typeHandlerVersion: '9.5'
//     settings: {
//     }
//   }
// }

// resource scaleset_Microsoft_Azure_Geneva_GenevaMonitoring 'Microsoft.Compute/virtualMachineScaleSets/extensions@2022-03-01' = {
//   parent: scaleset
//   name: 'Microsoft.Azure.Geneva.GenevaMonitoring'
//   properties: {
//     autoUpgradeMinorVersion: true
//     enableAutomaticUpgrade: true
//     publisher: 'Microsoft.Azure.Geneva'
//     type: 'GenevaMonitoring'
//     typeHandlerVersion: '2.0'
//     settings: {
//     }
//   }
// }

// resource scaleset_Microsoft_Azure_Security_AntimalwareSignature_AntimalwareConfiguration 'Microsoft.Compute/virtualMachineScaleSets/extensions@2022-03-01' = {
//   parent: scaleset
//   name: 'Microsoft.Azure.Security.AntimalwareSignature.AntimalwareConfiguration'
//   properties: {
//     autoUpgradeMinorVersion: true
//     enableAutomaticUpgrade: true
//     publisher: 'Microsoft.Azure.Security.AntimalwareSignature'
//     type: 'AntimalwareConfiguration'
//     typeHandlerVersion: '2.0'
//     settings: {
//     }
//   }
// }

// resource scaleset_OMSExtension 'Microsoft.Compute/virtualMachineScaleSets/extensions@2022-03-01' = {
//   parent: scaleset
//   name: 'OMSExtension'
//   properties: {
//     autoUpgradeMinorVersion: true
//     enableAutomaticUpgrade: false
//     publisher: 'Microsoft.EnterpriseCloud.Monitoring'
//     type: 'MicrosoftMonitoringAgent'
//     typeHandlerVersion: '1.0'
//     settings: {
//       workspaceId: '77da01e8-838a-41d9-a6d0-c8b180f68904'
//     }
//   }
// }

resource networkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2022-01-01' = {
  name: networkSecurityGroups_name
  location: location
  tags: {
  }
  properties: {
    securityRules: [
      {
        name: 'NRMS-Rule-109'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'DO NOT DELETE - Will result in ICM Sev 2 - Azure Core Security, see aka.ms/cainsgpolicy'
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 109
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: [
            '119'
            '137'
            '138'
            '139'
            '161'
            '162'
            '389'
            '636'
            '2049'
            '2301'
            '2381'
            '3268'
            '5800'
            '5900'
          ]
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
      {
        name: 'NRMS-Rule-104'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'Created by Azure Core Security managed policy, rule can be deleted but do not change source ips, please see aka.ms/cainsgpolicy'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'CorpNetSaw'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 104
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: []
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
      {
        name: 'NRMS-Rule-105'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'DO NOT DELETE - Will result in ICM Sev 2 - Azure Core Security, see aka.ms/cainsgpolicy'
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 105
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: [
            '1433'
            '1434'
            '3306'
            '4333'
            '5432'
            '6379'
            '7000'
            '7001'
            '7199'
            '9042'
            '9160'
            '9300'
            '16379'
            '26379'
            '27017'
          ]
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
      {
        name: 'NRMS-Rule-107'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'DO NOT DELETE - Will result in ICM Sev 2 - Azure Core Security, see aka.ms/cainsgpolicy'
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 107
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: [
            '23'
            '135'
            '445'
            '5985'
            '5986'
          ]
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
      {
        name: 'NRMS-Rule-106'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'DO NOT DELETE - Will result in ICM Sev 2 - Azure Core Security, see aka.ms/cainsgpolicy'
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 106
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: [
            '22'
            '3389'
          ]
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
      {
        name: 'NRMS-Rule-108'
        type: 'Microsoft.Network/networkSecurityGroups/securityRules'
        properties: {
          description: 'DO NOT DELETE - Will result in ICM Sev 2 - Azure Core Security, see aka.ms/cainsgpolicy'
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 108
          direction: 'Inbound'
          sourcePortRanges: []
          destinationPortRanges: [
            '13'
            '17'
            '19'
            '53'
            '69'
            '111'
            '123'
            '512'
            '514'
            '593'
            '873'
            '1900'
            '5353'
            '11211'
          ]
          sourceAddressPrefixes: []
          destinationAddressPrefixes: []
        }
      }
    ]
  }
}

resource Vnet 'Microsoft.Network/virtualNetworks@2022-05-01' = {
  name: 'vnet'

  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/8'
      ]
    }
    subnets: [
      {
        name: 'subnet'
        properties: {
          addressPrefix: '10.0.0.0/16'
          networkSecurityGroup: {
            id: networkSecurityGroup.id
          }
          delegations: []
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
        type: 'Microsoft.Network/virtualNetworks/subnets'
      }
    ]
    virtualNetworkPeerings: []
    enableDdosProtection: false
  }
}

resource subnet 'Microsoft.Network/virtualNetworks/subnets@2022-05-01' = {
  name: 'vnet/subnet'
  properties: {
    addressPrefix: '10.0.0.0/16'
    networkSecurityGroup: {
      id: networkSecurityGroup.id
    }
    delegations: []
    privateEndpointNetworkPolicies: 'Disabled'
    privateLinkServiceNetworkPolicies: 'Enabled'
  }
  dependsOn: [
    Vnet
  ]
}

// resource storageAccount 'Microsoft.Storage/storageAccounts@2022-05-01' = {
//   name: storage_acc
//   location: location
//   sku: {
//     name: 'Standard_LRS'
//   }
//   kind: 'StorageV2'
//   properties: {
//     supportsHttpsTrafficOnly: true
//     networkAcls: {
//       bypass: 'AzureServices'
//       defaultAction: 'Allow'
//       ipRules: []
//       virtualNetworkRules: []
//     }
//     encryption: {
//       services: {
//         blob: {
//           enabled: true
//         }
//         file: {
//           enabled: true
//         }
//         queue: {
//           enabled: true
//         }
//         table: {
//           enabled: true
//         }
//       }
//       keySource: 'Microsoft.Storage'
//     }
//     accessTier: 'Hot'
//   }
// }

