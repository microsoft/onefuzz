using System;
using System.Linq;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Async = System.Threading.Tasks;

namespace Tests.Fakes;


// TestContext provides a minimal IOnefuzzContext implementation to allow running
// of functions as unit or integration tests.
public sealed class TestContext : IOnefuzzContext {
    public TestContext(ILogTracer logTracer, IStorage storage, string tablePrefix, string accountId) {
        ServiceConfiguration = new TestServiceConfiguration(tablePrefix, accountId);

        Storage = storage;

        RequestHandling = new RequestHandling(logTracer);
        TaskOperations = new TaskOperations(logTracer, this);
        NodeOperations = new NodeOperations(logTracer, this);
        JobOperations = new JobOperations(logTracer, this);
        NodeTasksOperations = new NodeTasksOperations(logTracer, this);
        TaskEventOperations = new TaskEventOperations(logTracer, this);
    }

    public TestEvents Events { get; set; } = new();

    // convenience method for test setup
    public Async.Task InsertAll(params EntityBase[] objs)
        => Async.Task.WhenAll(
            objs.Select(x => x switch {
                Task t => TaskOperations.Insert(t),
                Node n => NodeOperations.Insert(n),
                Job j => JobOperations.Insert(j),
                NodeTasks nt => NodeTasksOperations.Insert(nt),
                _ => throw new NotImplementedException($"Need to add an TestContext.InsertAll case for {x.GetType()} entities"),
            }));

    // Implementations:

    IEvents IOnefuzzContext.Events => Events;

    public IServiceConfig ServiceConfiguration { get; }

    public IStorage Storage { get; }

    public IRequestHandling RequestHandling { get; }

    public ITaskOperations TaskOperations { get; }
    public IJobOperations JobOperations { get; }
    public INodeOperations NodeOperations { get; }
    public INodeTasksOperations NodeTasksOperations { get; }
    public ITaskEventOperations TaskEventOperations { get; }

    // -- Remainder not implemented --

    public IConfig Config => throw new System.NotImplementedException();

    public IConfigOperations ConfigOperations => throw new System.NotImplementedException();

    public IContainers Containers => throw new System.NotImplementedException();

    public ICreds Creds => throw new System.NotImplementedException();

    public IDiskOperations DiskOperations => throw new System.NotImplementedException();

    public IExtensions Extensions => throw new System.NotImplementedException();

    public IIpOperations IpOperations => throw new System.NotImplementedException();


    public ILogAnalytics LogAnalytics => throw new System.NotImplementedException();

    public INodeMessageOperations NodeMessageOperations => throw new System.NotImplementedException();

    public INotificationOperations NotificationOperations => throw new System.NotImplementedException();

    public IPoolOperations PoolOperations => throw new System.NotImplementedException();

    public IProxyForwardOperations ProxyForwardOperations => throw new System.NotImplementedException();

    public IProxyOperations ProxyOperations => throw new System.NotImplementedException();

    public IQueue Queue => throw new System.NotImplementedException();

    public IReports Reports => throw new System.NotImplementedException();

    public IReproOperations ReproOperations => throw new System.NotImplementedException();

    public IScalesetOperations ScalesetOperations => throw new System.NotImplementedException();

    public IScheduler Scheduler => throw new System.NotImplementedException();

    public ISecretsOperations SecretsOperations => throw new System.NotImplementedException();

    public IUserCredentials UserCredentials => throw new System.NotImplementedException();

    public IVmOperations VmOperations => throw new System.NotImplementedException();

    public IVmssOperations VmssOperations => throw new System.NotImplementedException();

    public IWebhookMessageLogOperations WebhookMessageLogOperations => throw new System.NotImplementedException();

    public IWebhookOperations WebhookOperations => throw new System.NotImplementedException();

}
