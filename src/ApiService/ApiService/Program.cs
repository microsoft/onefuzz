// to avoid collision with Task in model.cs
global using Async = System.Threading.Tasks;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ApiService.OneFuzzLib;



namespace Microsoft.OneFuzz.Service;

public class Program
{
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
                    _ => throw new Exception(string.Format("Unhandled Log Destination type: {0}", dest)),
                }
            );
        }
        return loggers;
    }


    public static void Main()
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
        .ConfigureServices((context, services) =>
            services
            .AddSingleton<ILogTracerFactory>(_ => new LogTracerFactory(GetLoggers()))
            .AddSingleton<INodeOperations, NodeOperations>()
            .AddSingleton<IEvents, Events>()
            .AddSingleton<IWebhookOperations, WebhookOperations>()
            .AddSingleton<IWebhookMessageLogOperations, WebhookMessageLogOperations>()
            .AddSingleton<ITaskOperations, TaskOperations>()
            .AddSingleton<IQueue, Queue>()
            .AddSingleton<ICreds>(_ => new Creds())
            .AddSingleton<IStorage, Storage>()
            .AddSingleton<IProxyOperations, ProxyOperations>()
        )
        .Build();

        host.Run();
    }
}
