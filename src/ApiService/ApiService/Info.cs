using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public record FunctionInfo(string Name, string ResourceGroup, string? SlotName);


public class Info
{

    private readonly ILogTracerFactory _loggerFactory;


    public Info(ILogTracerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    [Function("Info")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());
        log.Info("Creating function info response");
        var response = req.CreateResponse();
        FunctionInfo info = new(
                $"{EnvironmentVariables.OneFuzz.InstanceName}",
                $"{EnvironmentVariables.OneFuzz.ResourceGroup}",
                Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME"));


        log.Info("Returning function info");
        await response.WriteAsJsonAsync(info);
        log.Info("Returned function info");
        return response;
    }

}
