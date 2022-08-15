﻿// to avoid collision with Task in model.cs
global using System;
global
using System.Collections.Generic;
global
using System.Linq;
global
using Async = System.Threading.Tasks;
using System.Text.Json;
using Azure.Core.Serialization;
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


    public static List<ILog> GetLoggers(IServiceConfig config) {
        List<ILog> loggers = new List<ILog>();
        foreach (var dest in config.LogDestinations) {
            loggers.Add(
                dest switch {
                    LogDestination.AppInsights => new AppInsights(config.ApplicationInsightsInstrumentationKey!),
                    LogDestination.Console => new Console(),
                    _ => throw new Exception($"Unhandled Log Destination type: {dest}"),
                }
            );
        }
        return loggers;
    }


    //Move out expensive resources into separate class, and add those as Singleton
    // ArmClient, Table Client(s), Queue Client(s), HttpClient, etc.
    public async static Async.Task Main() {
        var host = new HostBuilder()
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
            .AddScoped<IOnefuzzContext, OnefuzzContext>()
            .AddScoped<IEndpointAuthorization, EndpointAuthorization>()
            .AddScoped<INodeMessageOperations, NodeMessageOperations>()
            .AddScoped<ISubnet, Subnet>()

            .AddSingleton<ICreds, Creds>()
            .AddSingleton<IServiceConfig, ServiceConfiguration>()
            .AddSingleton<IStorage, Storage>()
            .AddSingleton<ILogSinks, LogSinks>()
            .AddHttpClient()
            .AddMemoryCache();
        }
        )
        .Build();

        await host.RunAsync();
    }


}
