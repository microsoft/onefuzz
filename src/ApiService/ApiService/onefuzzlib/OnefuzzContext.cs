using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


namespace Microsoft.OneFuzz.Service;

using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

public interface IOnefuzzContext {
    IAutoScaleOperations AutoScaleOperations { get; }
    IConfig Config { get; }
    IConfigOperations ConfigOperations { get; }
    IContainers Containers { get; }
    ICreds Creds { get; }
    IDiskOperations DiskOperations { get; }
    IEvents Events { get; }
    IMetrics Metrics { get; }
    IExtensions Extensions { get; }
    IIpOperations IpOperations { get; }
    IJobOperations JobOperations { get; }
    IJobResultOperations JobResultOperations { get; }
    ILogAnalytics LogAnalytics { get; }
    INodeMessageOperations NodeMessageOperations { get; }
    INodeOperations NodeOperations { get; }
    INodeTasksOperations NodeTasksOperations { get; }
    INotificationOperations NotificationOperations { get; }
    IPoolOperations PoolOperations { get; }
    IProxyForwardOperations ProxyForwardOperations { get; }
    IProxyOperations ProxyOperations { get; }
    IQueue Queue { get; }
    IReports Reports { get; }
    IReproOperations ReproOperations { get; }
    IScalesetOperations ScalesetOperations { get; }
    IScheduler Scheduler { get; }
    ISecretsOperations SecretsOperations { get; }
    IServiceConfig ServiceConfiguration { get; }
    IStorage Storage { get; }
    ITaskOperations TaskOperations { get; }
    ITaskEventOperations TaskEventOperations { get; }
    IVmOperations VmOperations { get; }
    IVmssOperations VmssOperations { get; }
    IWebhookMessageLogOperations WebhookMessageLogOperations { get; }
    IWebhookOperations WebhookOperations { get; }
    IRequestHandling RequestHandling { get; }
    INsgOperations NsgOperations { get; }
    ISubnet Subnet { get; }
    EntityConverter EntityConverter { get; }
    ITeams Teams { get; }
    IGithubIssues GithubIssues { get; }
    IAdo Ado { get; }
    IJobCrashReportedOperations JobCrashReportedOperations { get; }
    IFeatureManagerSnapshot FeatureManagerSnapshot { get; }
    IConfigurationRefresher ConfigurationRefresher { get; }
}

public class OnefuzzContext : IOnefuzzContext {
    private readonly IServiceProvider _serviceProvider;
    public OnefuzzContext(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }

    public IAutoScaleOperations AutoScaleOperations => _serviceProvider.GetRequiredService<IAutoScaleOperations>();
    public INodeOperations NodeOperations => _serviceProvider.GetRequiredService<INodeOperations>();
    public IEvents Events => _serviceProvider.GetRequiredService<IEvents>();
    public IMetrics Metrics => _serviceProvider.GetRequiredService<IMetrics>();
    public IWebhookOperations WebhookOperations => _serviceProvider.GetRequiredService<IWebhookOperations>();
    public IWebhookMessageLogOperations WebhookMessageLogOperations => _serviceProvider.GetRequiredService<IWebhookMessageLogOperations>();
    public ITaskOperations TaskOperations => _serviceProvider.GetRequiredService<ITaskOperations>();
    public ITaskEventOperations TaskEventOperations => _serviceProvider.GetRequiredService<ITaskEventOperations>();
    public IQueue Queue => _serviceProvider.GetRequiredService<IQueue>();
    public IStorage Storage => _serviceProvider.GetRequiredService<IStorage>();
    public IProxyOperations ProxyOperations => _serviceProvider.GetRequiredService<IProxyOperations>();
    public IProxyForwardOperations ProxyForwardOperations => _serviceProvider.GetRequiredService<IProxyForwardOperations>();
    public IConfigOperations ConfigOperations => _serviceProvider.GetRequiredService<IConfigOperations>();
    public IScalesetOperations ScalesetOperations => _serviceProvider.GetRequiredService<IScalesetOperations>();
    public IContainers Containers => _serviceProvider.GetRequiredService<IContainers>();
    public IReports Reports => _serviceProvider.GetRequiredService<IReports>();
    public INotificationOperations NotificationOperations => _serviceProvider.GetRequiredService<INotificationOperations>();
    public IReproOperations ReproOperations => _serviceProvider.GetRequiredService<IReproOperations>();
    public IPoolOperations PoolOperations => _serviceProvider.GetRequiredService<IPoolOperations>();
    public IIpOperations IpOperations => _serviceProvider.GetRequiredService<IIpOperations>();
    public IDiskOperations DiskOperations => _serviceProvider.GetRequiredService<IDiskOperations>();
    public IVmOperations VmOperations => _serviceProvider.GetRequiredService<IVmOperations>();
    public ISecretsOperations SecretsOperations => _serviceProvider.GetRequiredService<ISecretsOperations>();
    public IJobOperations JobOperations => _serviceProvider.GetRequiredService<IJobOperations>();
    public IJobResultOperations JobResultOperations => _serviceProvider.GetRequiredService<IJobResultOperations>();
    public IScheduler Scheduler => _serviceProvider.GetRequiredService<IScheduler>();
    public IConfig Config => _serviceProvider.GetRequiredService<IConfig>();
    public ILogAnalytics LogAnalytics => _serviceProvider.GetRequiredService<ILogAnalytics>();
    public IExtensions Extensions => _serviceProvider.GetRequiredService<IExtensions>();
    public IVmssOperations VmssOperations => _serviceProvider.GetRequiredService<IVmssOperations>();
    public INodeTasksOperations NodeTasksOperations => _serviceProvider.GetRequiredService<INodeTasksOperations>();
    public INodeMessageOperations NodeMessageOperations => _serviceProvider.GetRequiredService<INodeMessageOperations>();
    public ICreds Creds => _serviceProvider.GetRequiredService<ICreds>();
    public IServiceConfig ServiceConfiguration => _serviceProvider.GetRequiredService<IServiceConfig>();
    public IRequestHandling RequestHandling => _serviceProvider.GetRequiredService<IRequestHandling>();
    public INsgOperations NsgOperations => _serviceProvider.GetRequiredService<INsgOperations>();
    public ISubnet Subnet => _serviceProvider.GetRequiredService<ISubnet>();
    public EntityConverter EntityConverter => _serviceProvider.GetRequiredService<EntityConverter>();
    public ITeams Teams => _serviceProvider.GetRequiredService<ITeams>();
    public IGithubIssues GithubIssues => _serviceProvider.GetRequiredService<IGithubIssues>();
    public IAdo Ado => _serviceProvider.GetRequiredService<IAdo>();
    public IJobCrashReportedOperations JobCrashReportedOperations => _serviceProvider.GetRequiredService<IJobCrashReportedOperations>();

    public IFeatureManagerSnapshot FeatureManagerSnapshot => _serviceProvider.GetRequiredService<IFeatureManagerSnapshot>();

    public IConfigurationRefresher ConfigurationRefresher => _serviceProvider.GetRequiredService<IConfigurationRefresherProvider>().Refreshers.First();
}
