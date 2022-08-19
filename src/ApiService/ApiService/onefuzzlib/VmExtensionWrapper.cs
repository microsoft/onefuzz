using Azure.Core;
using Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service {
    public class VMExtensionWrapper {
        public AzureLocation? Location { get; init; }
        public string? Name { get; init; }
        public string? TypePropertiesType { get; init; }
        public string? Publisher { get; init; }
        public string? TypeHandlerVersion { get; init; }
        public string? ForceUpdateTag { get; init; }
        public bool? AutoUpgradeMinorVersion { get; init; }
        public bool? EnableAutomaticUpgrade { get; init; }
        public BinaryData? Settings { get; init; }
        public BinaryData? ProtectedSettings { get; init; }

        public (string, VirtualMachineExtensionData) GetAsVirtualMachineExtension() {
            if (Location == null) { // EnsureNotNull does not satisfy the nullability checker
                throw new ArgumentNullException("Location required for VirtualMachineExtension");
            }
            TypePropertiesType.EnsureNotNull("TypePropertiesType required for VirtualMachineExtension");
            Publisher.EnsureNotNull("Publisher required for VirtualMachineExtension");
            TypeHandlerVersion.EnsureNotNull("TypeHandlerVersion required for VirtualMachineExtension");
            AutoUpgradeMinorVersion.EnsureNotNull("AutoUpgradeMinorVersion required for VirtualMachineExtension");

            var settings = Settings ?? new BinaryData(new Dictionary<string, string>());
            var protectedSettings = ProtectedSettings ?? new BinaryData(new Dictionary<string, string>());

            return (Name!, new VirtualMachineExtensionData(Location.Value) {
                TypePropertiesType = TypePropertiesType,
                Publisher = Publisher,
                TypeHandlerVersion = TypeHandlerVersion,
                AutoUpgradeMinorVersion = AutoUpgradeMinorVersion,
                EnableAutomaticUpgrade = EnableAutomaticUpgrade,
                ForceUpdateTag = ForceUpdateTag,
                Settings = settings,
                ProtectedSettings = protectedSettings
            });
        }

        public VirtualMachineScaleSetExtensionData GetAsVirtualMachineScaleSetExtension() {
            Name.EnsureNotNull("Name required for VirtualMachineScaleSetExtension");
            TypePropertiesType.EnsureNotNull("TypePropertiesType required for VirtualMachineScaleSetExtension");
            Publisher.EnsureNotNull("Publisher required for VirtualMachineScaleSetExtension");
            TypeHandlerVersion.EnsureNotNull("TypeHandlerVersion required for VirtualMachineScaleSetExtension");
            AutoUpgradeMinorVersion.EnsureNotNull("AutoUpgradeMinorVersion required for VirtualMachineScaleSetExtension");

            var settings = Settings ?? new BinaryData(new Dictionary<string, string>());
            var protectedSettings = ProtectedSettings ?? new BinaryData(new Dictionary<string, string>());

            return new VirtualMachineScaleSetExtensionData() {
                Name = Name,
                TypePropertiesType = TypePropertiesType,
                Publisher = Publisher,
                TypeHandlerVersion = TypeHandlerVersion,
                AutoUpgradeMinorVersion = AutoUpgradeMinorVersion,
                EnableAutomaticUpgrade = EnableAutomaticUpgrade,
                ForceUpdateTag = ForceUpdateTag,
                Settings = settings,
                ProtectedSettings = protectedSettings
            };
        }
    }

}
