using System.Threading.Tasks;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Pool {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Pool(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("Pool")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "PATCH" => Patch(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            var m => throw new InvalidOperationException("Unsupported HTTP method {m}"),
        });

    private async Task<HttpResponseData> Delete(HttpRequestData r) {
        var request = await RequestHandling.ParseRequest<PoolStop>(r);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(r, request.ErrorV, "PoolDelete");
        }

        var answer = await _auth.CheckRequireAdmins(r);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(r, answer.ErrorV, "PoolDelete");
        }

        var poolResult = await _context.PoolOperations.GetByName(request.OkV.Name);
        if (!poolResult.IsOk) {
            return await _context.RequestHandling.NotOk(r, poolResult.ErrorV, "pool stop");
        }

        await _context.PoolOperations.SetShutdown(poolResult.OkV, Now: request.OkV.Now);
        return await RequestHandling.Ok(r, true);
    }

    private Task<HttpResponseData> Post(HttpRequestData r) {
        throw new NotImplementedException();
    }

    private Task<HttpResponseData> Patch(HttpRequestData r) {
        throw new NotImplementedException();
    }

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<PoolSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "pool get");
        }

        var search = request.OkV;
        if (search.Name is not null) {
            var poolResult = await _context.PoolOperations.GetByName(search.Name);
            if (!poolResult.IsOk) {
                return await _context.RequestHandling.NotOk(req, poolResult.ErrorV, context: search.Name.ToString());
            }

            return await RequestHandling.Ok(req, await Populate(PoolToPoolResponse(poolResult.OkV)));
        }

        if (search.PoolId is Guid poolId) {
            var poolResult = await _context.PoolOperations.GetById(poolId);
            if (!poolResult.IsOk) {
                return await _context.RequestHandling.NotOk(req, poolResult.ErrorV, context: poolId.ToString());
            }

            return await RequestHandling.Ok(req, await Populate(PoolToPoolResponse(poolResult.OkV)));
        }

        if (search.State is not null) {
            var pools = await _context.PoolOperations.SearchStates(search.State).ToListAsync();
            return await RequestHandling.Ok(req, pools.Select(PoolToPoolResponse));
        }

        return await _context.RequestHandling.NotOk(
            req,
            new Error(
                ErrorCode.INVALID_REQUEST,
                new string[] { "at least one search option must be set" }),
            "pool get");
    }

    private static PoolGetResult PoolToPoolResponse(Service.Pool p)
        => new(
            Name: p.Name,
            PoolId: p.PoolId,
            Os: p.Os,
            State: p.State,
            ClientId: p.ClientId,
            Managed: p.Managed,
            Arch: p.Arch,
            Nodes: p.Nodes,
            Config: p.Config,
            WorkQueue: null,
            ScalesetSummary: null);

    private async Task<PoolGetResult> Populate(PoolGetResult p) {
        var (queueSas, instanceId, workQueue, scalesetSummary) = await (
            _context.Queue.GetQueueSas("node-heartbeat", StorageType.Config, QueueSasPermissions.Add),
            _context.Containers.GetInstanceId(),
            _context.PoolOperations.GetWorkQueue(p.PoolId, p.State),
            _context.PoolOperations.GetScalesetSummary(p.Name));

        return p with {
            WorkQueue = workQueue,
            ScalesetSummary = scalesetSummary,
            Config =
                new AgentConfig(
                    PoolName: p.Name,
                    OneFuzzUrl: _context.Creds.GetInstanceUrl(),
                    InstanceTelemetryKey: _context.ServiceConfiguration.ApplicationInsightsInstrumentationKey,
                    MicrosoftTelemetryKey: _context.ServiceConfiguration.OneFuzzTelemetry,
                    HeartbeatQueue: queueSas,
                    InstanceId: instanceId,
                    ClientCredentials: null,
                    MultiTenantDomain: _context.ServiceConfiguration.MultiTenantDomain)
        };
    }
}
