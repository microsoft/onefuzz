using System.Net;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

public class Webhooks {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public Webhooks(ILogger<Webhooks> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("Webhooks")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE", "PATCH")]
        HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req),
            "DELETE" => Delete(req),
            "PATCH" => Patch(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "webhook get");
        }

        if (request.OkV.WebhookId != null) {
            _log.LogInformation("getting webhook: {WebhookId}", request.OkV.WebhookId);
            var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId.Value);

            if (webhook == null) {
                return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook get");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(webhook);
            return response;
        }

        _log.LogInformation("listing webhooks");
        var webhooks = _context.WebhookOperations.SearchAll().Select(w => w with { Url = null, SecretToken = null });

        var response2 = req.CreateResponse(HttpStatusCode.OK);
        await response2.WriteAsJsonAsync(webhooks);
        return response2;


    }


    private async Async.Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookUpdate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "webhook update");
        }

        _log.LogInformation("updating webhook: {WebhookId}", request.OkV.WebhookId);

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook update");
        }

        var updated = webhook with {
            Url = request.OkV.Url ?? webhook.Url,
            Name = request.OkV.Name ?? webhook.Name,
            EventTypes = request.OkV.EventTypes ?? webhook.EventTypes,
            SecretToken = request.OkV.SecretToken ?? webhook.SecretToken,
            MessageFormat = request.OkV.MessageFormat ?? webhook.MessageFormat
        };

        var r = await _context.WebhookOperations.Replace(updated);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to replace webhook with updated entry {WebhookId}", updated.WebhookId);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(updated with { Url = null, SecretToken = null });
        return response;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "webhook create");
        }

        var webhook = new Webhook(Guid.NewGuid(), request.OkV.Name, request.OkV.Url, request.OkV.EventTypes,
            request.OkV.SecretToken, request.OkV.MessageFormat);

        var r = await _context.WebhookOperations.Insert(webhook);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to insert webhook {WebhookId}", webhook.WebhookId);
        }

        _log.LogInformation("added webhook: {WebhookId}", webhook.WebhookId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(webhook with { Url = null, SecretToken = null });
        return response;
    }


    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<WebhookGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "webhook delete");
        }

        _log.LogInformation("deleting webhook: {WebhookId}", request.OkV.WebhookId);

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook delete");
        }

        var r = await _context.WebhookOperations.Delete(webhook);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to delete webhook {WebhookName} - {WebhookId}", webhook.Name, webhook.WebhookId);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new BoolResult(true));
        return response;
    }
}
