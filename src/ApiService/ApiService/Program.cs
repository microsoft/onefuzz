// to avoid collision with Task in model.cs
global using Async = System.Threading.Tasks;

global using System;
global using System.Collections.Generic;
global using System.Linq;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;

public class Program
{
    public class LoggingMiddleware : IFunctionsWorkerMiddleware
    {
        public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var log = (ILogTracerInternal?)context.InstanceServices.GetService<ILogTracer>();
            if (log is not null)
            {
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


    public static List<ILog> GetLoggers()
    {
        List<ILog> loggers = new List<ILog>();
        foreach (var dest in EnvironmentVariables.LogDestinations)
        {
            loggers.Add(
                dest switch
                {
                    LogDestination.AppInsights => new AppInsights(),
                    LogDestination.Console => new Console(),
                    _ => throw new Exception($"Unhandled Log Destination type: {dest}"),
                }
            );
        }
        return loggers;
    }


    public static void Main()
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults(
            builder =>
            {
                builder.UseMiddleware<LoggingMiddleware>();
            }
        )
        .ConfigureServices((context, services) =>
            services
            .AddScoped<ILogTracer>(s => new LogTracerFactory(GetLoggers()).CreateLogTracer(Guid.Empty, severityLevel: EnvironmentVariables.LogSeverityLevel()))
            .AddScoped<INodeOperations, NodeOperations>()
            .AddScoped<IEvents, Events>()
            .AddScoped<IWebhookOperations, WebhookOperations>()
            .AddScoped<IWebhookMessageLogOperations, WebhookMessageLogOperations>()
            .AddScoped<ITaskOperations, TaskOperations>()
            .AddScoped<IQueue, Queue>()
            .AddScoped<ICreds, Creds>()
            .AddScoped<IStorage, Storage>()
            .AddScoped<IProxyOperations, ProxyOperations>()
            .AddScoped<IConfigOperations, ConfigOperations>()
            .AddScoped<IScalesetOperations, ScalesetOperations>()
            .AddScoped<IContainers, Containers>()
            .AddScoped<IReports, Reports>()
            .AddScoped<INotificationOperations, NotificationOperations>()

        //TODO: move out expensive resources into separate class, and add those as Singleton
        // ArmClient, Table Client(s), Queue Client(s), HttpClient, etc.

        )
        .Build();

        host.Run();
    }
}
