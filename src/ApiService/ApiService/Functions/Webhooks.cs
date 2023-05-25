using System.Net;

namespace Microsoft.OneFuzz.Service.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

public class Webhooks {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public Webhooks(ILogTracer log, IOnefuzzContext context) {
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
            _log.Info($"getting webhook: {request.OkV.WebhookId:Tag:WebhookId}");
            var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId.Value);

            if (webhook == null) {
                return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook get");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(webhook);
            return response;
        }

        _log.Info($"listing webhooks");
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

        _log.Info($"updating webhook: {request.OkV.WebhookId:Tag:WebhookId}");

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
            _log.WithHttpStatus(r.ErrorV).Error($"failed to replace webhook with updated entry {updated.WebhookId:Tag:WebhookId}");
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
            _log.WithHttpStatus(r.ErrorV).Error($"failed to insert webhook {webhook.WebhookId:Tag:WebhookId}");
        }

        _log.Info($"added webhook: {webhook.WebhookId:Tag:WebhookId}");

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

        _log.Info($"deleting webhook: {request.OkV.WebhookId:Tag:WebhookId}");

        var webhook = await _context.WebhookOperations.GetByWebhookId(request.OkV.WebhookId);

        if (webhook == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find webhook"), "webhook delete");
        }

        var r = await _context.WebhookOperations.Delete(webhook);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to delete webhook {webhook.Name:Tag:WebhookName} - {webhook.WebhookId:Tag:WebhookId}");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new BoolResult(true));
        return response;
    }
}
