using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Config {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public Config(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("Config")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req) {
        return Get(req);
    }
    public async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        _log.Info($"getting endpoint config parameters");

        var endpointParams = new ConfigResponse(
                Authority: _context.ServiceConfiguration.Authority,
                ClientId: _context.ServiceConfiguration.CliAppId,
                TenantDomain: _context.ServiceConfiguration.TenantDomain);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(endpointParams);

        return response;
    }
}
