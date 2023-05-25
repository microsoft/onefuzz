using System.Net;

namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

public class WebhookPing {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public WebhookPing(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("WebhookPing")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.User, "POST", Route = "webhooks/ping")]
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

        _log.Info($"pinging webhook : {request.OkV.WebhookId:Tag:WebhookId}");
        EventPing ping = await _context.WebhookOperations.Ping(webhook);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(ping);
        return response;
    }
}
