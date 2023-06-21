using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class InstanceConfig {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public InstanceConfig(ILogger<InstanceConfig> log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    public const string Route = "instance_config";

    [Function("InstanceConfig")]
    [Authorize(Allow.User)]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", Route=Route)]
        HttpRequestData req) {
        _log.LogInformation("getting instance_config");
        var config = await _context.ConfigOperations.Fetch();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(config);
        return response;
    }

    [Function("InstanceConfig_Admin")]
    [Authorize(Allow.Admin)]
    public async Task<HttpResponseData> Post(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route=Route)]
        HttpRequestData req) {
        _log.LogInformation("getting instance_config");
        var request = await RequestHandling.ParseRequest<InstanceConfigUpdate>(req);

        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "instance_config update");
        }

        var config = await _context.ConfigOperations.Fetch();
        var updateNsg = false;
        if (request.OkV.config.ProxyNsgConfig is NetworkSecurityGroupConfig requestConfig
            && config.ProxyNsgConfig is NetworkSecurityGroupConfig currentConfig) {
            if (!requestConfig.AllowedServiceTags.ToHashSet().SetEquals(currentConfig.AllowedServiceTags)
                || !requestConfig.AllowedIps.ToHashSet().SetEquals(currentConfig.AllowedIps)) {
                updateNsg = true;
            }
        }

        await _context.ConfigOperations.Save(request.OkV.config, false, false);

        if (updateNsg) {
            await foreach (var nsg in _context.NsgOperations.ListNsgs()) {
                _log.LogInformation("Checking if nsg: {Location} ({NsgName}) owned by OneFuzz", nsg.Data.Location!, nsg.Data.Name);
                if (nsg.Data.Location! == nsg.Data.Name) {
                    var result = await _context.NsgOperations.SetAllowedSources(new Nsg(nsg.Data.Location!, nsg.Data.Location!), request.OkV.config.ProxyNsgConfig!);
                    if (!result.IsOk) {
                        return await _context.RequestHandling.NotOk(
                        req,
                        result.ErrorV,
                        context: "instance_config update");
                    }
                }
            }
        }
        var instanceConfigResponse = req.CreateResponse(HttpStatusCode.OK);
        await instanceConfigResponse.WriteAsJsonAsync(request.OkV.config);
        return instanceConfigResponse;
    }
}
