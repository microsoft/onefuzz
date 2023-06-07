﻿using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class NotificationsTest {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public NotificationsTest(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("NotificationsTest")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "notifications/test")] HttpRequestData req) {
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
}
