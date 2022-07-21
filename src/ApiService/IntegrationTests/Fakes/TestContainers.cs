using System;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;

// TestContainers class allows use of InstanceID without having to set it up in blob storage
class TestContainers : Containers {
    public TestContainers(ILogTracer log, IStorage storage, ICreds creds, IServiceConfig config)
        : base(log, storage, creds, config) { }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public override Task<Guid> GetInstanceId()
        => System.Threading.Tasks.Task.FromResult(InstanceId);
}
