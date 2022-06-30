using System.Web;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

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
        var machineId = uri["machine_id"];
        if (machineId is null || !Guid.TryParse(machineId, out var machineGuid)) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'machine_id' query parameter must be provided" }),
                "agent registration");
        }

        var agentNode = await _context.NodeOperations.GetByMachineId(machineGuid);
        if (agentNode is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { $"unable to find a registration associated with machine_id '{machineGuid}'" }),
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

        return await RequestHandling.Ok(req, await CreateRegistrationResponse(machineGuid, pool.OkV));
    }

    private async Async.Task<AgentRegistrationResponse> CreateRegistrationResponse(Guid machineId, Pool pool) {
        var baseAddress = _context.Creds.GetInstanceUrl();
        var eventsUrl = new Uri(baseAddress, "/api/agents/events");
        var commandsUrl = new Uri(baseAddress, "/api/agents/commands");

        var workQueue = await _context.Queue.GetQueueSas(
            _context.PoolOperations.GetPoolQueue(pool),
            StorageType.Corpus,
            QueueSasPermissions.Read | QueueSasPermissions.Update | QueueSasPermissions.Process,
            TimeSpan.FromHours(24));

        return new AgentRegistrationResponse(
            EventsUrl: eventsUrl,
            CommandsUrl: commandsUrl,
            WorkQueue: workQueue);
    }

    private Async.Task<HttpResponseData> Post(HttpRequestData req) {
        throw new NotImplementedException();
    }
}
