using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class AgentRegistration {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public AgentRegistration(ILogger<AgentRegistration> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("AgentRegistration")]
    [Authorize(Allow.Agent)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "GET", "POST",
            Route="agents/registration")] HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req),
            var m => throw new InvalidOperationException($"method {m} not supported"),
        };

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseUri<AgentRegistrationGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "agent registration");
        }

        var machineId = request.OkV.MachineId;

        if (machineId == Guid.Empty) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    "'machine_id' query parameter must be provided"),
                "agent registration");
        }

        var agentNode = await _context.NodeOperations.GetByMachineId(machineId);
        if (agentNode is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    $"unable to find a registration associated with machine_id '{machineId}'"),
                "agent registration");
        }

        var pool = await _context.PoolOperations.GetByName(agentNode.PoolName);
        if (!pool.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    "unable to find a pool associated with the provided machine_id"),
                "agent registration");
        }

        return await RequestHandling.Ok(req, await CreateRegistrationResponse(pool.OkV));
    }

    private async Async.Task<AgentRegistrationResponse> CreateRegistrationResponse(Service.Pool pool) {
        var baseAddress = _context.Creds.GetInstanceUrl();
        var eventsUrl = new Uri(baseAddress, "/api/agents/events");
        var commandsUrl = new Uri(baseAddress, "/api/agents/commands");
        var workQueue = await _context.Queue.GetQueueSas(
            _context.PoolOperations.GetPoolQueue(pool.PoolId),
            StorageType.Corpus,
            QueueSasPermissions.Read | QueueSasPermissions.Update | QueueSasPermissions.Process,
            TimeSpan.FromHours(24));

        return new AgentRegistrationResponse(
            EventsUrl: eventsUrl,
            CommandsUrl: commandsUrl,
            WorkQueue: workQueue);
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseUri<AgentRegistrationPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "agent registration");
        }

        var machineId = request.OkV.MachineId;
        var poolName = request.OkV.PoolName;
        var scalesetId = request.OkV.ScalesetId;
        var version = request.OkV.Version;
        var os = request.OkV.Os;
        var machineName = request.OkV.MachineName;

        if (machineId == Guid.Empty) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST, "'machine_id' query parameter must be provided"),
                "agent registration");
        }

        if (poolName is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST, "'pool_name' query parameter must be provided"),
                "agent registration");
        }

        var instanceId = machineName is not null ? InstanceIds.InstanceIdFromMachineName(machineName) : null;


        _log.LogInformation("registration request: {MachineId} {PoolName} {ScalesetId} {Version}", machineId, poolName, scalesetId, version);
        var poolResult = await _context.PoolOperations.GetByName(poolName);
        if (!poolResult.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST, $"unable to find pool '{poolName}'"),
                "agent registration");
        }

        var pool = poolResult.OkV;

        var existingNode = await _context.NodeOperations.GetByMachineId(machineId);
        if (existingNode is not null) {
            await _context.NodeOperations.Delete(existingNode, "Node is re registering");
        }

        if (os != null && pool.Os != os) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    $"OS mismatch: pool '{poolName}' is configured for '{pool.Os}', but agent is running '{os}'"),
                "agent registration");
        }

        var node = new Service.Node(
            PoolName: poolName,
            PoolId: pool.PoolId,
            MachineId: machineId,
            ScalesetId: scalesetId,
            InstanceId: instanceId,
            Version: version,
            Os: os ?? pool.Os,
            Managed: pool.Managed
            );

        var r = await _context.NodeOperations.Replace(node);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to replace node operations for {MachineId}", node.MachineId);
        }

        return await RequestHandling.Ok(req, await CreateRegistrationResponse(pool));
    }
}
