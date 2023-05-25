using System.Net;

namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

public class WebhookLogs {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public WebhookLogs(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("WebhookLogs")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.User, "POST", Route = "webhooks/logs")]
        HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "webhook log");
        }

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook log");
        }

        _log.Info($"getting webhook logs: {request.OkV.WebhookId:Tag:WebhookId}");
        var logs = _context.WebhookMessageLogOperations.SearchByPartitionKeys(new[] { $"{request.OkV.WebhookId}" });

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(logs);
        return response;
    }
}
