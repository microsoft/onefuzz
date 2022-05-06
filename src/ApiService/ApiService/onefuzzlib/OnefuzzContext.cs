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
    IUserCredentials UserCredentials { get; }
    IVmOperations VmOperations { get; }
    IVmssOperations VmssOperations { get; }
    IWebhookMessageLogOperations WebhookMessageLogOperations { get; }
    IWebhookOperations WebhookOperations { get; }
}

public class OnefuzzContext : IOnefuzzContext {

    private readonly IServiceProvider _serviceProvider;
    public INodeOperations NodeOperations { get => _serviceProvider.GetService<INodeOperations>() ?? throw new Exception("No INodeOperations service"); }
    public IEvents Events { get => _serviceProvider.GetService<IEvents>() ?? throw new Exception("No IEvents service"); }
    public IWebhookOperations WebhookOperations { get => _serviceProvider.GetService<IWebhookOperations>() ?? throw new Exception("No IWebhookOperations service"); }
    public IWebhookMessageLogOperations WebhookMessageLogOperations { get => _serviceProvider.GetService<IWebhookMessageLogOperations>() ?? throw new Exception("No IWebhookMessageLogOperations service"); }
    public ITaskOperations TaskOperations { get => _serviceProvider.GetService<ITaskOperations>() ?? throw new Exception("No ITaskOperations service"); }
    public IQueue Queue { get => _serviceProvider.GetService<IQueue>() ?? throw new Exception("No IQueue service"); }
    public IStorage Storage { get => _serviceProvider.GetService<IStorage>() ?? throw new Exception("No IStorage service"); }
    public IProxyOperations ProxyOperations { get => _serviceProvider.GetService<IProxyOperations>() ?? throw new Exception("No IProxyOperations service"); }
    public IProxyForwardOperations ProxyForwardOperations { get => _serviceProvider.GetService<IProxyForwardOperations>() ?? throw new Exception("No IProxyForwardOperations service"); }
    public IConfigOperations ConfigOperations { get => _serviceProvider.GetService<IConfigOperations>() ?? throw new Exception("No IConfigOperations service"); }
    public IScalesetOperations ScalesetOperations { get => _serviceProvider.GetService<IScalesetOperations>() ?? throw new Exception("No IScalesetOperations service"); }
    public IContainers Containers { get => _serviceProvider.GetService<IContainers>() ?? throw new Exception("No IContainers service"); }
    public IReports Reports { get => _serviceProvider.GetService<IReports>() ?? throw new Exception("No IReports service"); }
    public INotificationOperations NotificationOperations { get => _serviceProvider.GetService<INotificationOperations>() ?? throw new Exception("No INotificationOperations service"); }
    public IUserCredentials UserCredentials { get => _serviceProvider.GetService<IUserCredentials>() ?? throw new Exception("No IUserCredentials service"); }
    public IReproOperations ReproOperations { get => _serviceProvider.GetService<IReproOperations>() ?? throw new Exception("No IReproOperations service"); }
    public IPoolOperations PoolOperations { get => _serviceProvider.GetService<IPoolOperations>() ?? throw new Exception("No IPoolOperations service"); }
    public IIpOperations IpOperations { get => _serviceProvider.GetService<IIpOperations>() ?? throw new Exception("No IIpOperations service"); }
    public IDiskOperations DiskOperations { get => _serviceProvider.GetService<IDiskOperations>() ?? throw new Exception("No IDiskOperations service"); }
    public IVmOperations VmOperations { get => _serviceProvider.GetService<IVmOperations>() ?? throw new Exception("No IVmOperations service"); }
    public ISecretsOperations SecretsOperations { get => _serviceProvider.GetService<ISecretsOperations>() ?? throw new Exception("No ISecretsOperations service"); }
    public IJobOperations JobOperations { get => _serviceProvider.GetService<IJobOperations>() ?? throw new Exception("No IJobOperations service"); }
    public IScheduler Scheduler { get => _serviceProvider.GetService<IScheduler>() ?? throw new Exception("No IScheduler service"); }
    public IConfig Config { get => _serviceProvider.GetService<IConfig>() ?? throw new Exception("No IConfig service"); }
    public ILogAnalytics LogAnalytics { get => _serviceProvider.GetService<ILogAnalytics>() ?? throw new Exception("No ILogAnalytics service"); }
    public IExtensions Extensions { get => _serviceProvider.GetService<IExtensions>() ?? throw new Exception("No IExtensions service"); }
    public IVmssOperations VmssOperations { get => _serviceProvider.GetService<IVmssOperations>() ?? throw new Exception("No IVmssOperations service"); }
    public INodeTasksOperations NodeTasksOperations { get => _serviceProvider.GetService<INodeTasksOperations>() ?? throw new Exception("No INodeTasksOperations service"); }
    public INodeMessageOperations NodeMessageOperations { get => _serviceProvider.GetService<INodeMessageOperations>() ?? throw new Exception("No INodeMessageOperations service"); }
    public ICreds Creds { get => _serviceProvider.GetService<ICreds>() ?? throw new Exception("No ICreds service"); }
    public IServiceConfig ServiceConfiguration { get => _serviceProvider.GetService<IServiceConfig>() ?? throw new Exception("No IServiceConfiguration service"); }

    public OnefuzzContext(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}

