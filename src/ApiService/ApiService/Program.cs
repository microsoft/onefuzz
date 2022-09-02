﻿// to avoid collision with Task in model.cs
global using System;
global
using System.Collections.Generic;
global
using System.Linq;
global
using Async = System.Threading.Tasks;
using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Core.Serialization;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class Program {
    public class LoggingMiddleware : IFunctionsWorkerMiddleware {
        public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next) {
            var log = (ILogTracerInternal?)context.InstanceServices.GetService<ILogTracer>();
            if (log is not null) {
                //TODO
                //if correlation ID is available in HTTP request
                //if correlation ID is available in Queue message
                //log.ReplaceCorrelationId(Guid from request)

                log.ReplaceCorrelationId(Guid.NewGuid());
                log.AddTags(new[] {
                    ("InvocationId", context.InvocationId.ToString())
                });
            }

            await next(context);
        }
    }

    //Move out expensive resources into separate class, and add those as Singleton
    // ArmClient, Table Client(s), Queue Client(s), HttpClient, etc.
    public static async Async.Task Main() {
        using var host =
            new HostBuilder()
            .ConfigureFunctionsWorkerDefaults(
                builder => {
                    builder.UseMiddleware<LoggingMiddleware>();
                }
            )
            .ConfigureServices((context, services) => {
                services.Configure<JsonSerializerOptions>(options => {
                    options = EntityConverter.GetJsonSerializerOptions();
                });

                services.Configure<WorkerOptions>(options => {
                    options.Serializer = new JsonObjectSerializer(EntityConverter.GetJsonSerializerOptions());
                });

                services
                .AddScoped<ILogTracer>(s => {
                    var logSinks = s.GetRequiredService<ILogSinks>();
                    var cfg = s.GetRequiredService<IServiceConfig>();
                    return new LogTracerFactory(logSinks.GetLogSinks())
                        .CreateLogTracer(
                            Guid.Empty,
                            severityLevel: cfg.LogSeverityLevel);
                })
                .AddScoped<IAutoScaleOperations, AutoScaleOperations>()
                .AddScoped<INodeOperations, NodeOperations>()
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
                .AddScoped<IUserCredentials, UserCredentials>()
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
                .AddScoped<IImageOperations, ImageOperations>()
                .AddScoped<ITeams, Teams>()
                .AddScoped<IGithubIssues, GithubIssues>()
                .AddScoped<IOnefuzzContext, OnefuzzContext>()
                .AddScoped<IEndpointAuthorization, EndpointAuthorization>()
                .AddScoped<INodeMessageOperations, NodeMessageOperations>()
                .AddScoped<ISubnet, Subnet>()
                .AddScoped<IAutoScaleOperations, AutoScaleOperations>()

                .AddSingleton<TelemetryConfiguration>(provider => {
                    var config = provider.GetRequiredService<IServiceConfig>();
                    return new() {
                        ConnectionString = $"InstrumentationKey={config.ApplicationInsightsInstrumentationKey}",
                    };
                })
                .AddSingleton<DependencyTrackingTelemetryModule>()
                .AddSingleton<ICreds, Creds>()
                .AddSingleton<EntityConverter>()
                .AddSingleton<IServiceConfig, ServiceConfiguration>()
                .AddSingleton<IStorage, Storage>()
                .AddSingleton<ILogSinks, LogSinks>()
                .AddHttpClient()
                .AddMemoryCache();
            })
            .Build();

        // Set up Application Insights dependency tracking:
        {
            var telemetryConfig = host.Services.GetRequiredService<TelemetryConfiguration>();
            var module = host.Services.GetRequiredService<DependencyTrackingTelemetryModule>();
            module.Initialize(telemetryConfig);
        }

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
            await Async.Task.WhenAll(toCreate.Select(t => tableClient.CreateTableIfNotExistsAsync(serviceConfig.OneFuzzStoragePrefix + t.Name)));
        }
    }
}
