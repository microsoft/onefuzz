using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;

// TestContainers class allows use of InstanceID without having to set it up in blob storage
sealed class TestContainers : Containers {
    public TestContainers(ILogger<Containers> log, IStorage storage, IServiceConfig config, IOnefuzzContext context, IMemoryCache cache)
        : base(log, storage, config, context, cache) { }
}
