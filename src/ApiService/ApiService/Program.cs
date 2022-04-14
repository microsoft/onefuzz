// to avoid collision with Task in model.cs
global using Async = System.Threading.Tasks;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ApiService.OneFuzzLib;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;

public class Program
{
    public class LoggingMiddleware : IFunctionsWorkerMiddleware
    {
        public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            //TODO
            //if correlation ID is available in HTTP request
            //if correlation ID is available in Queue message 
            //log.ReplaceCorrelationId

            var log = (ILogTracerInternal?)context.InstanceServices.GetService<ILogTracer>();
            if (log is not null)
            {
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
            .AddScoped<ILogTracer>(_ => new LogTracerFactory(GetLoggers()).CreateLogTracer(Guid.NewGuid()))
            .AddSingleton<INodeOperations, NodeOperations>()
            .AddSingleton<IEvents, Events>()
            .AddSingleton<IWebhookOperations, WebhookOperations>()
            .AddSingleton<IWebhookMessageLogOperations, WebhookMessageLogOperations>()
            .AddSingleton<ITaskOperations, TaskOperations>()
            .AddSingleton<IQueue, Queue>()
            .AddSingleton<ICreds, Creds>()
            .AddSingleton<IStorage, Storage>()
            .AddSingleton<IProxyOperations, ProxyOperations>()
            .AddSingleton<IConfigOperations, ConfigOperations>()
        )
        .Build();

        host.Run();
    }
}
