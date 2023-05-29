using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;


// TestContext provides a minimal IOnefuzzContext implementation to allow running
// of functions as unit or integration tests.
public sealed class TestContext : IOnefuzzContext {
    public TestContext(IHttpClientFactory httpClientFactory, ILogTracer logTracer, IStorage storage, ICreds creds, string storagePrefix) {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        EntityConverter = new EntityConverter();
        ServiceConfiguration = new TestServiceConfiguration(storagePrefix);
        Storage = storage;
        Creds = creds;

        // this one is faked entirely; we can’t perform these operations at test time
        VmssOperations = new TestVmssOperations();

        Containers = new Containers(logTracer, Storage, ServiceConfiguration);
        Queue = new Queue(Storage, logTracer);
        RequestHandling = new RequestHandling(logTracer);
        TaskOperations = new TaskOperations(logTracer, cache, this);
        NodeOperations = new NodeOperations(logTracer, this);
        JobOperations = new JobOperations(logTracer, this);
        NodeTasksOperations = new NodeTasksOperations(logTracer, this);
        TaskEventOperations = new TaskEventOperations(logTracer, this);
        NodeMessageOperations = new NodeMessageOperations(logTracer, this);
        ConfigOperations = new ConfigOperations(logTracer, this, cache);
        PoolOperations = new PoolOperations(logTracer, this);
        ScalesetOperations = new ScalesetOperations(logTracer, cache, this);
        ReproOperations = new ReproOperations(logTracer, this);
        Reports = new Reports(logTracer, Containers);
        NotificationOperations = new NotificationOperations(logTracer, this);
        SecretsOperations = new TestSecretsOperations(Creds, ServiceConfiguration);
        FeatureManagerSnapshot = new TestFeatureManagerSnapshot();
        WebhookOperations = new TestWebhookOperations(httpClientFactory, logTracer, this);
        Events = new TestEvents(logTracer, this);
        Metrics = new TestMetrics(logTracer, this);
        WebhookMessageLogOperations = new TestWebhookMessageLogOperations(logTracer, this);
    }

    // convenience method for test setup
    public Async.Task InsertAll(params EntityBase[] objs)
        => Async.Task.WhenAll(
            objs.Select(x => x switch {
                Task t => TaskOperations.Insert(t),
                Node n => NodeOperations.Insert(n),
                Pool p => PoolOperations.Insert(p),
                Job j => JobOperations.Insert(j),
                Repro r => ReproOperations.Insert(r),
                Scaleset ss => ScalesetOperations.Insert(ss),
                NodeTasks nt => NodeTasksOperations.Insert(nt),
                InstanceConfig ic => ConfigOperations.Insert(ic),
                Notification n => NotificationOperations.Insert(n),
                Webhook w => WebhookOperations.Insert(w),
                _ => throw new NotSupportedException($"You will need to add an TestContext.InsertAll case for {x.GetType()} entities"),
            }));

    // Implementations:

    public IEvents Events { get; }
    public IMetrics Metrics { get; }

    public IServiceConfig ServiceConfiguration { get; }

    public IStorage Storage { get; }
    public ICreds Creds { get; }
    public IContainers Containers { get; set; }
    public IQueue Queue { get; }

    public IRequestHandling RequestHandling { get; }

    public ITaskOperations TaskOperations { get; }
    public IJobOperations JobOperations { get; }
    public INodeOperations NodeOperations { get; }
    public INodeTasksOperations NodeTasksOperations { get; }
    public ITaskEventOperations TaskEventOperations { get; }
    public INodeMessageOperations NodeMessageOperations { get; }
    public IConfigOperations ConfigOperations { get; }
    public IPoolOperations PoolOperations { get; }
    public IScalesetOperations ScalesetOperations { get; }
    public IVmssOperations VmssOperations { get; }
    public IReproOperations ReproOperations { get; }
    public IReports Reports { get; }
    public EntityConverter EntityConverter { get; }

    public INotificationOperations NotificationOperations { get; }

    public ISecretsOperations SecretsOperations { get; }

    public IFeatureManagerSnapshot FeatureManagerSnapshot { get; }

    public IWebhookOperations WebhookOperations { get; }

    public IWebhookMessageLogOperations WebhookMessageLogOperations { get; }

    // -- Remainder not implemented --

    public IConfig Config => throw new System.NotImplementedException();

    public IAutoScaleOperations AutoScaleOperations => throw new NotImplementedException();

    public IDiskOperations DiskOperations => throw new System.NotImplementedException();

    public IExtensions Extensions => throw new System.NotImplementedException();

    public IIpOperations IpOperations => throw new System.NotImplementedException();

    public ILogAnalytics LogAnalytics => throw new System.NotImplementedException();


    public IProxyForwardOperations ProxyForwardOperations => throw new System.NotImplementedException();

    public IProxyOperations ProxyOperations => throw new System.NotImplementedException();


    public IScheduler Scheduler => throw new System.NotImplementedException();


    public IVmOperations VmOperations => throw new System.NotImplementedException();

    public INsgOperations NsgOperations => throw new NotImplementedException();

    public ISubnet Subnet => throw new NotImplementedException();

    public ITeams Teams => throw new NotImplementedException();
    public IGithubIssues GithubIssues => throw new NotImplementedException();
    public IAdo Ado => throw new NotImplementedException();

    public IConfigurationRefresher ConfigurationRefresher => throw new NotImplementedException();
}
