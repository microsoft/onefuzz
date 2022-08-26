using System.Web;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class AgentRegistration {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public AgentRegistration(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("AgentRegistration")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "GET", "POST",
            Route="agents/registration")] HttpRequestData req)
        => _auth.CallIfAgent(
            req,
            r => r.Method switch {
                "GET" => Get(r),
                "POST" => Post(r),
                var m => throw new InvalidOperationException($"method {m} not supported"),
            });

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var uri = HttpUtility.ParseQueryString(req.Url.Query);
        var rawMachineId = uri["machine_id"];
        if (rawMachineId is null || !Guid.TryParse(rawMachineId, out var machineId)) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'machine_id' query parameter must be provided" }),
                "agent registration");
        }

        var agentNode = await _context.NodeOperations.GetByMachineId(machineId);
        if (agentNode is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { $"unable to find a registration associated with machine_id '{machineId}'" }),
                "agent registration");
        }

        var pool = await _context.PoolOperations.GetByName(agentNode.PoolName);
        if (!pool.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "unable to find a pool associated with the provided machine_id" }),
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
        var uri = HttpUtility.ParseQueryString(req.Url.Query);
        var rawMachineId = uri["machine_id"];
        if (rawMachineId is null || !Guid.TryParse(rawMachineId, out var machineId)) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'machine_id' query parameter must be provided" }),
                "agent registration");
        }

        var rawPoolName = uri["pool_name"];
        if (rawPoolName is null || !PoolName.TryParse(rawPoolName, out var poolName)) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'pool_name' query parameter must be provided" }),
                "agent registration");
        }

        var rawScalesetId = uri["scaleset_id"];
        var scalesetId = rawScalesetId is null ? (Guid?)null : Guid.Parse(rawScalesetId);

        var version = uri["version"] ?? "1.0.0";

        _log.Info($"registration request: machine_id: {machineId} pool_name: {poolName} scaleset_id: {scalesetId} version: {version}");
        var poolResult = await _context.PoolOperations.GetByName(poolName);
        if (!poolResult.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.INVALID_REQUEST,
                    Errors: new[] { $"unable to find pool '{poolName}'" }),
                "agent registration");
        }

        var pool = poolResult.OkV;

        var existingNode = await _context.NodeOperations.GetByMachineId(machineId);
        if (existingNode is not null) {
            await _context.NodeOperations.Delete(existingNode);
        }

        var node = new Service.Node(
            PoolName: poolName,
            PoolId: pool.PoolId,
            MachineId: machineId,
            ScalesetId: scalesetId,
            Version: version);

        await _context.NodeOperations.Replace(node);

        return await RequestHandling.Ok(req, await CreateRegistrationResponse(pool));
    }
}
