using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class Notifications {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public Notifications(ILogger<Notifications> log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        _log.AddTag("HttpRequest", "GET");
        _log.LogInformation("Notification search");
        var request = await RequestHandling.ParseRequest<NotificationSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "notification search");
        }

        var entries = request.OkV switch { { Container: null, NotificationId: null } => _context.NotificationOperations.SearchAll(), { Container: var c, NotificationId: null } => _context.NotificationOperations.SearchByRowKeys(c.Select(x => x.String)), { Container: var _, NotificationId: var n } => new[] { await _context.NotificationOperations.GetNotification(n.Value) }.ToAsyncEnumerable()
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries);
        return response;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        _log.AddTag("HttpRequest", "POST");
        _log.LogInformation("adding notification hook");
        var request = await RequestHandling.ParseRequest<NotificationCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "notification create");
        }

        var notificationRequest = request.OkV;

        var entry = await _context.NotificationOperations.Create(notificationRequest.Container, notificationRequest.Config,
            notificationRequest.ReplaceExisting);

        if (!entry.IsOk) {
            return await _context.RequestHandling.NotOk(req, entry.ErrorV, context: "notification create");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entry.OkV);
        return response;
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NotificationGet>(req);

        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "notification delete");
        }
        var entries = await _context.NotificationOperations.SearchByPartitionKeys(new[] { $"{request.OkV.NotificationId}" }).ToListAsync();

        if (entries.Count == 0) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find notification"), context: "notification delete");
        }

        if (entries.Count > 1) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "error identifying Notification"), context: "notification delete");
        }

        var result = await _context.NotificationOperations.Delete(entries[0]);

        if (!result.IsOk) {
            var (status, error) = result.ErrorV;
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.UNABLE_TO_UPDATE, error), "notification delete");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries[0]);
        return response;
    }


    [Function("Notifications")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req),
            "DELETE" => Delete(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };
}
