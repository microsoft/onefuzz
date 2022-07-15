using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service {
    public class VMExtenionWrapper {
        public AzureLocation? Location {get; init; }
        public string? Name {get; init; }
        public string? TypePropertiesType {get; init; }
        public string? Publisher {get; init; }
        public string? TypeHandlerVersion {get; init; }
        public bool? AutoUpgradeMinorVersion {get; init; }
        public BinaryData? Settings {get; init; }
        public BinaryData? ProtectedSettings {get; init; }

        public (string, VirtualMachineExtensionData) GetAsVirtualMachineExtension() {
            Location.EnsureNotNull("Location required for VirtualMachineExtension");
            TypePropertiesType.EnsureNotNull("TypePropertiesType required for VirtualMachineExtension");
            Publisher.EnsureNotNull("Publisher required for VirtualMachineExtension");
            TypeHandlerVersion.EnsureNotNull("TypeHandlerVersion required for VirtualMachineExtension");
            AutoUpgradeMinorVersion.EnsureNotNull("AutoUpgradeMinorVersion required for VirtualMachineExtension");
            Settings.EnsureNotNull("Settings required for VirtualMachineExtension");
            ProtectedSettings.EnsureNotNull("ProtectedSettings required for VirtualMachineExtension");
            #pragma warning disable CS1503
            return (Name!, new VirtualMachineExtensionData(Location) {
                TypePropertiesType=TypeHandlerVersion,
                Publisher=Publisher,
                TypeHandlerVersion=TypeHandlerVersion,
                AutoUpgradeMinorVersion=AutoUpgradeMinorVersion,
                Settings=Settings,
                ProtectedSettings=ProtectedSettings
            });
            #pragma warning restore CS1503
        }

        public VirtualMachineScaleSetExtensionData GetAsVirtualMachineScaleSetExtension() {
            Name.EnsureNotNull("Name required for VirtualMachineScaleSetExtension");
            TypePropertiesType.EnsureNotNull("TypePropertiesType required for VirtualMachineScaleSetExtension");
            Publisher.EnsureNotNull("Publisher required for VirtualMachineScaleSetExtension");
            TypeHandlerVersion.EnsureNotNull("TypeHandlerVersion required for VirtualMachineScaleSetExtension");
            AutoUpgradeMinorVersion.EnsureNotNull("AutoUpgradeMinorVersion required for VirtualMachineScaleSetExtension");
            Settings.EnsureNotNull("Settings required for VirtualMachineScaleSetExtension");
            ProtectedSettings.EnsureNotNull("ProtectedSettings required for VirtualMachineScaleSetExtension");
            #pragma warning disable CS1503
            return new VirtualMachineScaleSetExtensionData() {
                Name=Name,
                TypePropertiesType=TypeHandlerVersion,
                Publisher=Publisher,
                TypeHandlerVersion=TypeHandlerVersion,
                AutoUpgradeMinorVersion=AutoUpgradeMinorVersion,
                Settings=Settings,
                ProtectedSettings=ProtectedSettings
            };
            #pragma warning restore CS1503
        }

    }

}

