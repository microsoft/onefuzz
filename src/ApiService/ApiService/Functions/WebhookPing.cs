using System.Net;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

public class WebhookPing {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public WebhookPing(ILogger<WebhookPing> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("WebhookPing")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "webhooks/ping")]
        HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "webhook ping");
        }

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook ping");
        }

        _log.LogInformation("pinging webhook : {WebhookId}", request.OkV.WebhookId);
        EventPing ping = await _context.WebhookOperations.Ping(webhook);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(ping);
        return response;
    }
}
