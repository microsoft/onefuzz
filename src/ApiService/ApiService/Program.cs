using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker.Configuration;
using Azure.ResourceManager.Storage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class Program
{
    public static List<ILog> GetLoggers() {
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
            .AddScoped<ILogTracer>(s => s.GetService<LogTracerFactory>()?.MakeLogTracer(Guid.NewGuid()) ?? throw new InvalidOperationException("Unable to create a logger") )
            .AddSingleton<IStorageProvider>(_ => new StorageProvider(EnvironmentVariables.OneFuzz.FuncStorage ?? throw new InvalidOperationException("Missing account id") ))
            .AddSingleton<ICreds>(_ => new Creds())
            .AddSingleton<IStorage, Storage>()
        )
        .Build();

        host.Run();
    }
}
