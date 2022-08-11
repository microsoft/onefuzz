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
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
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

    private async Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<PoolCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "PoolCreate");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "PoolCreate");
        }

        var create = request.OkV;
        var pool = await _context.PoolOperations.GetByName(create.Name);
        if (pool.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.INVALID_REQUEST,
                    Errors: new string[] { "pool with that name already exists" }),
                "PoolCreate");
        }

        // logging.Info(request)

        var newPool = new Service.Pool(
            PoolId: Guid.NewGuid(),
            State: PoolState.Init,
            Name: create.Name,
            Os: create.Os,
            Managed: create.Managed,
            Arch: create.Arch);

        await _context.PoolOperations.Insert(newPool);
        return await RequestHandling.Ok(req, await Populate(PoolToPoolResponse(newPool), true));
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

        var pools = await _context.PoolOperations.SearchStates(search.State ?? Enumerable.Empty<PoolState>()).ToListAsync();
        return await RequestHandling.Ok(req, pools.Select(PoolToPoolResponse));
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

    private async Task<PoolGetResult> Populate(PoolGetResult p, bool skipSummaries = false) {
        var (queueSas, instanceId, workQueue, scalesetSummary) = await (
            _context.Queue.GetQueueSas("node-heartbeat", StorageType.Config, QueueSasPermissions.Add),
            _context.Containers.GetInstanceId(),
            skipSummaries ? Async.Task.FromResult(new List<WorkSetSummary>()) : _context.PoolOperations.GetWorkQueue(p.PoolId, p.State),
            skipSummaries ? Async.Task.FromResult(new List<ScalesetSummary>()) : _context.PoolOperations.GetScalesetSummary(p.Name));

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
