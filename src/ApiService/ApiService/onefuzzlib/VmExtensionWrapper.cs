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

        public VirtualMachineScaleSetExtensionData GetAsVirtualMachineScaleSetExtension() {
            return new VirtualMachineScaleSetExtensionData() {
                Name = Name.EnsureNotNull("Name required for VirtualMachineScaleSetExtension"),
                TypePropertiesType = TypePropertiesType.EnsureNotNull("TypePropertiesType required for VirtualMachineScaleSetExtension"),
                Publisher = Publisher.EnsureNotNull("Publisher required for VirtualMachineScaleSetExtension"),
                TypeHandlerVersion = TypeHandlerVersion.EnsureNotNull("TypeHandlerVersion required for VirtualMachineScaleSetExtension"),
                AutoUpgradeMinorVersion = AutoUpgradeMinorVersion.EnsureNotNull("AutoUpgradeMinorVersion required for VirtualMachineScaleSetExtension"),
                EnableAutomaticUpgrade = EnableAutomaticUpgrade,
                ForceUpdateTag = ForceUpdateTag,
                Settings = Settings ?? new BinaryData(new Dictionary<string, string>()),
                ProtectedSettings = ProtectedSettings ?? new BinaryData(new Dictionary<string, string>()),
            };
        }
    }
}
