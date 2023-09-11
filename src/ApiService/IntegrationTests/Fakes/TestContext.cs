using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Tests;
using Async = System.Threading.Tasks;
namespace IntegrationTests.Fakes;


// TestContext provides a minimal IOnefuzzContext implementation to allow running
// of functions as unit or integration tests.
public sealed class TestContext : IOnefuzzContext {
    public TestContext(IHttpClientFactory httpClientFactory, OneFuzzLoggerProvider provider, IStorage storage, ICreds creds, string storagePrefix) {
        Cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        ServiceConfiguration = new TestServiceConfiguration(storagePrefix);
        Storage = storage;
        Creds = creds;
        SecretsOperations = new TestSecretOperations();
        EntityConverter = new EntityConverter(SecretsOperations);

        // this one is faked entirely; we can’t perform these operations at test time
        VmssOperations = new TestVmssOperations();

        Containers = new Containers(provider.CreateLogger<Containers>(), Storage, ServiceConfiguration, this, Cache);
        Queue = new Queue(Storage, provider.CreateLogger<Queue>());
        RequestHandling = new RequestHandling(provider.CreateLogger<RequestHandling>());
        TaskOperations = new TaskOperations(provider.CreateLogger<TaskOperations>(), Cache, this);
        NodeOperations = new NodeOperations(provider.CreateLogger<NodeOperations>(), this);
        JobOperations = new JobOperations(provider.CreateLogger<JobOperations>(), this);
        JobResultOperations = new JobResultOperations(provider.CreateLogger<JobResultOperations>(), this);
        NodeTasksOperations = new NodeTasksOperations(provider.CreateLogger<NodeTasksOperations>(), this);
        TaskEventOperations = new TaskEventOperations(provider.CreateLogger<TaskEventOperations>(), this);
        NodeMessageOperations = new NodeMessageOperations(provider.CreateLogger<NodeMessageOperations>(), this);
        ConfigOperations = new ConfigOperations(provider.CreateLogger<ConfigOperations>(), this, Cache);
        PoolOperations = new PoolOperations(provider.CreateLogger<PoolOperations>(), this);
        ScalesetOperations = new ScalesetOperations(provider.CreateLogger<ScalesetOperations>(), Cache, this);
        Reports = new Reports(provider.CreateLogger<Reports>(), Containers);
        NotificationOperations = new NotificationOperations(provider.CreateLogger<NotificationOperations>(), this);

        FeatureManagerSnapshot = new TestFeatureManagerSnapshot();
        WebhookOperations = new TestWebhookOperations(httpClientFactory, provider.CreateLogger<WebhookOperations>(), this);
        Events = new TestEvents(provider.CreateLogger<Events>(), this);
        Metrics = new TestMetrics(provider.CreateLogger<Metrics>(), this);
        WebhookMessageLogOperations = new TestWebhookMessageLogOperations(provider.CreateLogger<WebhookMessageLogOperations>(), this);
    }

    // convenience method for test setup
    public Async.Task InsertAll(params EntityBase[] objs)
        => Async.Task.WhenAll(
            objs.Select(x => x switch {
                Task t => TaskOperations.Insert(t),
                Node n => NodeOperations.Insert(n),
                Pool p => PoolOperations.Insert(p),
                Job j => JobOperations.Insert(j),
                JobResult jr => JobResultOperations.Insert(jr),
                Scaleset ss => ScalesetOperations.Insert(ss),
                NodeTasks nt => NodeTasksOperations.Insert(nt),
                InstanceConfig ic => ConfigOperations.Insert(ic),
                Notification n => NotificationOperations.Insert(n),
                Webhook w => WebhookOperations.Insert(w),
                _ => throw new NotSupportedException($"You will need to add an TestContext.InsertAll case for {x.GetType()} entities"),
            }));

    // Implementations:

    public IMemoryCache Cache { get; }

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
    public IJobResultOperations JobResultOperations { get; }
    public INodeOperations NodeOperations { get; }
    public INodeTasksOperations NodeTasksOperations { get; }
    public ITaskEventOperations TaskEventOperations { get; }
    public INodeMessageOperations NodeMessageOperations { get; }
    public IConfigOperations ConfigOperations { get; }
    public IPoolOperations PoolOperations { get; }
    public IScalesetOperations ScalesetOperations { get; }
    public IVmssOperations VmssOperations { get; }
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

    public ILogAnalytics LogAnalytics => throw new System.NotImplementedException();

    public IScheduler Scheduler => throw new System.NotImplementedException();

    public ISubnet Subnet => throw new NotImplementedException();

    public ITeams Teams => throw new NotImplementedException();
    public IGithubIssues GithubIssues => throw new NotImplementedException();
    public IAdo Ado => throw new NotImplementedException();

    public IConfigurationRefresher ConfigurationRefresher => throw new NotImplementedException();
}
