using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class InstanceConfig {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public InstanceConfig(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("InstanceConfig")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "POST" => Post(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }
    public async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        _log.Info($"getting instance_config");
        var config = await _context.ConfigOperations.Fetch();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(config);
        return response;
    }

    public async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<InstanceConfigUpdate>(req);

        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "instance_config update");
        }
        var config = await _context.ConfigOperations.Fetch();
        // Check if can modify
        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "instance_config update");
        }
        var updateNsg = false;
        if (request.OkV.config.ProxyNsgConfig is not null && config.ProxyNsgConfig is not null) {
            var requestConfig = request.OkV.config.ProxyNsgConfig;
            var currentConfig = config.ProxyNsgConfig;
            if ((new HashSet<string>(requestConfig.AllowedServiceTags)).SetEquals(new HashSet<string>(currentConfig.AllowedServiceTags)) || ((new HashSet<string>(requestConfig.AllowedIps)).SetEquals(new HashSet<string>(currentConfig.AllowedIps)))) {
                updateNsg = true;
            }
        }
        await _context.ConfigOperations.Save(request.OkV.config, false, false);
        if (updateNsg) {
            await foreach (var nsg in _context.NsgOperations.ListNsgs()) {
                _log.Info($"Checking if nsg: {nsg.Data.Location!} ({nsg.Data.Name}) owned by OneFuz");
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
        return await _context.RequestHandling.Ok(req, new InstanceConfigUpdate(config));
    }
}
