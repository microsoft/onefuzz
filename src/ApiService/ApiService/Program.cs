﻿global using System;
global using System.Collections.Generic;
global using System.Linq;
// to avoid collision with Task in model.cs
global using Async = System.Threading.Tasks;
using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Core.Serialization;
using Azure.Identity;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.Graph;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service;

public class Program {

    /// <summary>
    /// 
    /// </summary>
    public class LoggingMiddleware : IFunctionsWorkerMiddleware {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next) {
            //https://learn.microsoft.com/en-us/azure/azure-monitor/app/custom-operations-tracking#applicationinsights-operations-vs-systemdiagnosticsactivity
            using var activity = OneFuzzLogger.Activity;
            _ = activity.Start();
            string correlationId = Guid.NewGuid().ToString();

            if (await context.GetHttpRequestDataAsync() is HttpRequestData requestData) {
                //if header has 1f-CorrelationId then use that
                //otherwise check if message can be deserialized to {"correlationId": "SOME-GUID"}, then use that

                if (requestData.Headers.TryGetValues("Correlation-ID", out var values1f)) {
                    correlationId = values1f.First();
                } else if (requestData.Headers.TryGetValues("X-Correlation-ID", out var values)) {
                    correlationId = values.First();
                }
            }

            _ = activity.AddTag(OneFuzzLogger.CorrelationId, correlationId);
            _ = activity.AddTag("FunctionId", context.FunctionId);
            _ = activity.AddTag("InvocationId", context.InvocationId);

            await next(context);

            context.GetHttpResponseData()?.Headers.Add("X-Correlation-ID", correlationId);
        }
    }


    //Move out expensive resources into separate class, and add those as Singleton
    // ArmClient, Table Client(s), Queue Client(s), HttpClient, etc.
    public static async Async.Task Main() {
        var configuration = new ServiceConfiguration();

        using var host =
            new HostBuilder()

            .ConfigureAppConfiguration(builder => {
                // Using a connection string in dev allows us to run the functions locally.
                if (!string.IsNullOrEmpty(configuration.AppConfigurationConnectionString)) {
                    var _ = builder.AddAzureAppConfiguration(options => {
                        var _ = options
                            .Connect(configuration.AppConfigurationConnectionString)
                            .UseFeatureFlags(ffOptions => ffOptions.CacheExpirationInterval = TimeSpan.FromSeconds(30));
                    });
                } else {
                    var _ = builder.AddAzureAppConfiguration(options => {
                        var _ = options
                            .Connect(new Uri(configuration.AppConfigurationEndpoint!), new DefaultAzureCredential())
                            .UseFeatureFlags(ffOptions => ffOptions.CacheExpirationInterval = TimeSpan.FromMinutes(1));
                    });
                }
            })
            .ConfigureServices((context, services) => {
                services.Configure<JsonSerializerOptions>(options => {
                    options = EntityConverter.GetJsonSerializerOptions();
                });

                services.Configure<WorkerOptions>(options => {
                    options.Serializer = new JsonObjectSerializer(EntityConverter.GetJsonSerializerOptions());
                });

                services
                .AddScoped<IAutoScaleOperations, AutoScaleOperations>()
                .AddScoped<INodeOperations, NodeOperations>()
                .AddScoped<IMetrics, Metrics>()
                .AddScoped<IEvents, Events>()
                .AddScoped<IWebhookOperations, WebhookOperations>()
                .AddScoped<IWebhookMessageLogOperations, WebhookMessageLogOperations>()
                .AddScoped<ITaskOperations, TaskOperations>()
                .AddScoped<ITaskEventOperations, TaskEventOperations>()
                .AddScoped<IQueue, Queue>()
                .AddScoped<IProxyOperations, ProxyOperations>()
                .AddScoped<IProxyForwardOperations, ProxyForwardOperations>()
                .AddScoped<IConfigOperations, ConfigOperations>()
                .AddScoped<IScalesetOperations, ScalesetOperations>()
                .AddScoped<IContainers, Containers>()
                .AddScoped<IReports, Reports>()
                .AddScoped<INotificationOperations, NotificationOperations>()
                .AddScoped<IReproOperations, ReproOperations>()
                .AddScoped<IPoolOperations, PoolOperations>()
                .AddScoped<IIpOperations, IpOperations>()
                .AddScoped<IDiskOperations, DiskOperations>()
                .AddScoped<IVmOperations, VmOperations>()
                .AddScoped<ISecretsOperations, SecretsOperations>()
                .AddScoped<IJobOperations, JobOperations>()
                .AddScoped<INsgOperations, NsgOperations>()
                .AddScoped<IScheduler, Scheduler>()
                .AddScoped<IConfig, Config>()
                .AddScoped<ILogAnalytics, LogAnalytics>()
                .AddScoped<IExtensions, Extensions>()
                .AddScoped<IVmssOperations, VmssOperations>()
                .AddScoped<INodeTasksOperations, NodeTasksOperations>()
                .AddScoped<INodeMessageOperations, NodeMessageOperations>()
                .AddScoped<IRequestHandling, RequestHandling>()
                .AddScoped<ITeams, Teams>()
                .AddScoped<IGithubIssues, GithubIssues>()
                .AddScoped<IAdo, Ado>()
                .AddScoped<IOnefuzzContext, OnefuzzContext>()
                .AddScoped<IEndpointAuthorization, EndpointAuthorization>()
                .AddScoped<INodeMessageOperations, NodeMessageOperations>()
                .AddScoped<ISubnet, Subnet>()
                .AddScoped<IAutoScaleOperations, AutoScaleOperations>()
                .AddSingleton<GraphServiceClient>(new GraphServiceClient(new DefaultAzureCredential()))
                .AddSingleton<DependencyTrackingTelemetryModule>()
                .AddSingleton<ICreds, Creds>()
                .AddSingleton<EntityConverter>()
                .AddSingleton<IServiceConfig>(configuration)
                .AddSingleton<IStorage, Storage>()
                .AddHttpClient()
                .AddMemoryCache()
                .AddAzureAppConfiguration();

                _ = services.AddFeatureManagement();
            })
            .ConfigureLogging(loggingBuilder => {
                loggingBuilder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, OneFuzzLoggerProvider>(
                    x => {
                        var appInsightsConnectionString = $"InstrumentationKey={configuration.ApplicationInsightsInstrumentationKey}";
                        var tc = new ApplicationInsights.TelemetryClient(new ApplicationInsights.Extensibility.TelemetryConfiguration() { ConnectionString = appInsightsConnectionString });
                        return new OneFuzzLoggerProvider(new List<TelemetryConfig>() { new TelemetryConfig(tc) });
                    }
                    ));
            })
            .ConfigureFunctionsWorkerDefaults(builder => {
                builder.UseMiddleware<LoggingMiddleware>();
                builder.UseMiddleware<Auth.AuthenticationMiddleware>();
                builder.UseMiddleware<Auth.AuthorizationMiddleware>();

                //this is a must, to tell the host that worker logging is done by us
                builder.Services.Configure<WorkerOptions>(workerOptions => workerOptions.Capabilities["WorkerApplicationInsightsLoggingEnabled"] = bool.TrueString);
                builder.AddApplicationInsights(options => {
                    options.DeveloperMode = false;
                    options.EnableDependencyTrackingTelemetryModule = true;
                });
                builder.AddApplicationInsightsLogger();

            })
            .Build();

        // Initialize expected Storage tables:
        await SetupStorage(
            host.Services.GetRequiredService<IStorage>(),
            host.Services.GetRequiredService<IServiceConfig>());

        await host.RunAsync();
    }

    public static async Async.Task SetupStorage(IStorage storage, IServiceConfig serviceConfig) {
        // Creates the tables for each implementor of IOrm<T>

        // locate all IOrm<T> instances and collect the Ts
        var toCreate = new List<Type>();
        var types = typeof(Program).Assembly.GetTypes();
        foreach (var type in types) {
            if (type.IsAbstract) {
                continue;
            }

            foreach (var iface in type.GetInterfaces()) {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IOrm<>)) {
                    toCreate.Add(iface.GenericTypeArguments.Single());
                    break;
                }
            }
        }

        var storageAccount = serviceConfig.OneFuzzFuncStorage;
        if (storageAccount is not null) {
            var tableClient = await storage.GetTableServiceClientForAccount(storageAccount);
            await Async.Task.WhenAll(toCreate.Select(async t => {
                // don't care if it was created or not
                _ = await tableClient.CreateTableIfNotExistsAsync(serviceConfig.OneFuzzStoragePrefix + t.Name);
            }));
        }
    }
}
