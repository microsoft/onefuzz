using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Scaleset {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Scaleset(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("Scaleset")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "PATCH" => Patch(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

    private async Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetStop>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetDelete");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "ScalesetDelete");
        }

        var scalesetResult = await _context.ScalesetOperations.GetById(request.OkV.ScalesetId);
        if (!scalesetResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetDelete");
        }

        var scaleset = scalesetResult.OkV;
        await _context.ScalesetOperations.SetShutdown(scaleset, request.OkV.Now);
        return await RequestHandling.Ok(req, true);
    }

    private async Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetCreate");
        }

        throw new NotImplementedException();
    }

    private async Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetUpdate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetUpdate");
        }
        throw new NotImplementedException();
    }

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetSearch");
        }

        var search = request.OkV;
        if (search.ScalesetId is Guid id) {
            var scalesetResult = await _context.ScalesetOperations.GetById(id);
            if (!scalesetResult.IsOk) {
                return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetSearch");
            }

            var scaleset = scalesetResult.OkV;

            var response = ScalesetSearchResponse.ForScaleset(scaleset);
            response = response with { Nodes = await _context.ScalesetOperations.GetNodes(scaleset) };
            if (!search.IncludeAuth) {
                response = response with { Auth = null };
            }

            return await RequestHandling.Ok(req, response);
        }

        var states = search.State ?? Enumerable.Empty<ScalesetState>();
        var scalesets = await _context.ScalesetOperations.SearchStates(states).ToListAsync();
        // don't return auths during list actions, only 'get'
        var result = scalesets.Select(ss => ScalesetSearchResponse.ForScaleset(ss with { Auth = null }));
        return await RequestHandling.Ok(req, result);
    }
}
