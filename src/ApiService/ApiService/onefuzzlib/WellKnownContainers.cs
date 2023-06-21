
namespace Microsoft.OneFuzz.Service;

public static class WellKnownContainers {
    public static readonly Container BaseConfig = Container.Parse("base-config");
    public static readonly Container VmScripts = Container.Parse("vm-scripts");
    public static readonly Container InstanceSpecificSetup = Container.Parse("instance-specific-setup");
    public static readonly Container Tools = Container.Parse("tools");
    public static readonly Container ReproScripts = Container.Parse("repro-scripts");
    public static readonly Container TaskConfigs = Container.Parse("task-configs");
    public static readonly Container ProxyConfigs = Container.Parse("proxy-configs");
    public static readonly Container Events = Container.Parse("events");
}
