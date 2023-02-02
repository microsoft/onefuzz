using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Notifications {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Notifications(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        _log.WithTag("HttpRequest", "GET").Info($"Notification search");
        var request = await RequestHandling.ParseRequest<NotificationSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "notification search");
        }

        var entries = request.OkV switch { { Container: null, NotificationId: null } => _context.NotificationOperations.SearchAll(), { Container: var c, NotificationId: null } => _context.NotificationOperations.SearchByRowKeys(c.Select(x => x.String)), { Container: var _, NotificationId: var n } => new[] { await _context.NotificationOperations.GetNotification(n.Value) }.ToAsyncEnumerable(),
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries);
        return response;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        _log.WithTag("HttpRequest", "POST").Info($"adding notification hook");
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
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find notification" }), context: "notification delete");
        }

        if (entries.Count > 1) {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "error identifying Notification" }), context: "notification delete");
        }
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries[0]);
        return response;
    }


    [Function("Notifications")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }
}
