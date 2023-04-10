using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class EventsFunction {
    private readonly ILogTracer _logger;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public EventsFunction(ILogTracer logger, IEndpointAuthorization auth, IOnefuzzContext context) {
        _logger = logger;
        _auth = auth;
        _context = context;
    }

    [Function("Events")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            _ => throw new NotSupportedException(),
        });

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<EventsGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "events get");
        }

        var eventsGet = request.OkV;

        var requestedEvent = await _context.Events.GetEvent(eventsGet.EventId);
        return await RequestHandling.Ok(req, new EventGetResponse(requestedEvent));
    }
}