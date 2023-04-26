using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class NotificationsTest {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public NotificationsTest(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        _log.WithTag("HttpRequest", "GET").Info($"Notification test");
        var request = await RequestHandling.ParseRequest<NotificationTest>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "notification search");
        }

        var notificationTest = request.OkV;
        var result = await _context.NotificationOperations.TriggerNotification(notificationTest.Notification.Container, notificationTest.Notification,
            notificationTest.Report, isLastRetryAttempt: true);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new NotificationTestResponse(result.IsOk, result.ErrorV?.ToString()));
        return response;
    }


    [Function("NotificationsTest")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "notifications/test")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "POST" => Post(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }
}
