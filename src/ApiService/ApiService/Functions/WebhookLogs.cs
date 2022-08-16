using System.Net;

namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class WebhookLogs {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public WebhookLogs(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("Webhook_logs")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "webhooks/logs")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "POST" => Post(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "webhook log");
        }

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find webhook" }), "webhook log");
        }

        _log.Info($"getting webhook logs: {request.OkV.WebhookId}");
        var logs = _context.WebhookMessageLogOperations.SearchByPartitionKeys(new[] { $"{request.OkV.WebhookId}" });

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(logs);
        return response;
    }
}
