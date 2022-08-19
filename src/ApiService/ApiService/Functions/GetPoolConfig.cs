using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class GetPoolConfig {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public GetPoolConfig(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("GetPoolConfig")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", Route = "pool/getconfig")] HttpRequestData req, ClaimsPrincipal principal)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            var m => throw new InvalidOperationException("Unsupported HTTP method {m}"),
        });

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<PoolSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "pool get config");
        }

        var search = request.OkV;
        OneFuzzResult<Service.Pool> poolResult;
        if (search.PoolId is Guid poolId) {
            poolResult = await _context.PoolOperations.GetById(poolId);
        } else if (search.Name is PoolName name) {
            poolResult = await _context.PoolOperations.GetByName(name);
        } else {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "missing pool name or id" }), "pool get config");
        }

        if (!poolResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, poolResult.ErrorV, context: search.ToString());
        }

        if (!poolResult.OkV.Managed) {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "Pool is a managed pool" }), context: search.ToString());
        }
        var poolConfig = await _context.Extensions.CreatePoolConfig(poolResult.OkV);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(poolConfig);
        return response;
    }


}
