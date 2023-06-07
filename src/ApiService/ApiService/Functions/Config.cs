using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class Config {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public Config(ILogger<Config> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("Config")]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET")]
        HttpRequestData req) {

        _log.LogInformation($"getting endpoint config parameters");

        var endpointParams = new ConfigResponse(
                Authority: _context.ServiceConfiguration.Authority,
                ClientId: _context.ServiceConfiguration.CliAppId,
                TenantDomain: _context.ServiceConfiguration.TenantDomain,
                MultiTenantDomain: _context.ServiceConfiguration.MultiTenantDomain);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(endpointParams);

        return response;
    }
}
