﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class EventsFunction {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public EventsFunction(ILogTracer log, IOnefuzzContext context) {
        _context = context;
        _log = log;
    }

    [Function("Events")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.User, "GET")] HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<EventsGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "events get");
        }

        var eventsGet = request.OkV;

        var requestedEvent = await _context.Events.GetDownloadableEvent(eventsGet.EventId);
        if (!requestedEvent.IsOk) {
            return await _context.RequestHandling.NotOk(req, requestedEvent.ErrorV, "events get");
        }

        return await RequestHandling.Ok(req, new EventGetResponse(requestedEvent.OkV));
    }
}
