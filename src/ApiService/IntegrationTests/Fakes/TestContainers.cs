using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;

// TestContainers class allows use of InstanceID without having to set it up in blob storage
sealed class TestContainers : Containers {
    public TestContainers(ILogger<Containers> log, IStorage storage, IServiceConfig config, IOnefuzzContext context)
        : base(log, storage, config, context) { }
}
