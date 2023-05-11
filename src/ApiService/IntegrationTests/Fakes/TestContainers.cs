using Microsoft.OneFuzz.Service;

// TestContainers class allows use of InstanceID without having to set it up in blob storage
sealed class TestContainers : Containers {
    public TestContainers(ILogTracer log, IStorage storage, IServiceConfig config)
        : base(log, storage, config) { }
}
