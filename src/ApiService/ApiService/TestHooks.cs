using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public record FunctionInfo(string Name, string ResourceGroup, string? SlotName);


public class TestHooks
{

    private readonly ILogTracer _log;
    private readonly IConfigOperations _configOps;
    private readonly IEvents _events;

    public TestHooks(ILogTracer log, IConfigOperations configOps, IEvents events)
    {
        _log = log;
        _configOps = configOps;
        _events = events;
    }

    [Function("Info")]
    public async Task<HttpResponseData> Info([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/info")] HttpRequestData req)
    {
        _log.Info("Creating function info response");
        var response = req.CreateResponse();
        FunctionInfo info = new(
                $"{EnvironmentVariables.OneFuzz.InstanceName}",
                $"{EnvironmentVariables.OneFuzz.ResourceGroup}",
                Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME"));

        _log.Info("Returning function info");
        await response.WriteAsJsonAsync(info);
        _log.Info("Returned function info");
        return response;
    }

    [Function("InstanceConfig")]
    public async Task<HttpResponseData> InstanceConfig([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/instance-config")] HttpRequestData req)
    {
        _log.Info("Fetching instance config");
        var config = await _configOps.Fetch();

        if (config is null)
        {
            _log.Error("Instance config is null");
            Error err = new(ErrorCode.INVALID_REQUEST, new[] { "Instance config is null" });
            var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await resp.WriteAsJsonAsync(err);
            return resp;
        }
        else
        {
            await _events.SendEvent(new EventInstanceConfigUpdated(config));

            var str = (new EntityConverter()).ToJsonString(config);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(str);
            return resp;
        }
    }
}
