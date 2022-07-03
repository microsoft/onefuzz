namespace Microsoft.OneFuzz.Service;

using Microsoft.Extensions.DependencyInjection;

public interface IOnefuzzContext {
    IConfig Config { get; }
    IConfigOperations ConfigOperations { get; }
    IContainers Containers { get; }
    ICreds Creds { get; }
    IDiskOperations DiskOperations { get; }
    IEvents Events { get; }
    IExtensions Extensions { get; }
    IIpOperations IpOperations { get; }
    IJobOperations JobOperations { get; }
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
    IUserCredentials UserCredentials { get; }
    IVmOperations VmOperations { get; }
    IVmssOperations VmssOperations { get; }
    IWebhookMessageLogOperations WebhookMessageLogOperations { get; }
    IWebhookOperations WebhookOperations { get; }
    IRequestHandling RequestHandling { get; }
    INsgOperations NsgOperations { get; }
    ISubnet Subnet { get; }
}

public class OnefuzzContext : IOnefuzzContext {
    private readonly IServiceProvider _serviceProvider;
    public OnefuzzContext(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }

    public INodeOperations NodeOperations => _serviceProvider.GetRequiredService<INodeOperations>();
    public IEvents Events => _serviceProvider.GetRequiredService<IEvents>();
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
    public IUserCredentials UserCredentials => _serviceProvider.GetRequiredService<IUserCredentials>();
    public IReproOperations ReproOperations => _serviceProvider.GetRequiredService<IReproOperations>();
    public IPoolOperations PoolOperations => _serviceProvider.GetRequiredService<IPoolOperations>();
    public IIpOperations IpOperations => _serviceProvider.GetRequiredService<IIpOperations>();
    public IDiskOperations DiskOperations => _serviceProvider.GetRequiredService<IDiskOperations>();
    public IVmOperations VmOperations => _serviceProvider.GetRequiredService<IVmOperations>();
    public ISecretsOperations SecretsOperations => _serviceProvider.GetRequiredService<ISecretsOperations>();
    public IJobOperations JobOperations => _serviceProvider.GetRequiredService<IJobOperations>();
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
}
