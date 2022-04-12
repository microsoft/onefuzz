using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public record FunctionInfo(string Name, string ResourceGroup, string? SlotName);


public class TestHooks
{

    private readonly ILogTracerFactory _loggerFactory;
    private readonly IConfigOperations _configOps;

    public TestHooks(ILogTracerFactory loggerFactory, IConfigOperations configOps)
    {
        _loggerFactory = loggerFactory;
        _configOps = configOps;
    }

    [Function("Info")]
    public async Task<HttpResponseData> Info([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/info")] HttpRequestData req)
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


    [Function("InstanceConfig")]
    public async Task<HttpResponseData> InstanceConfig([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/instance-config")] HttpRequestData req)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());
        log.Info("Fetching instance config");
        var config = await _configOps.Fetch();

        if (config is null)
        {
            log.Error("Instance config is null");
            Error err = new(ErrorCode.INVALID_REQUEST, new[] { "Instance config is null" });
            var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await resp.WriteAsJsonAsync(err);
            return resp;
        }
        else
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(config);
            return resp;
        }
    }
}
