using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class Node {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public Node(ILogger<Node> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    public const string Route = "node";

    [Function("Node")]
    [Authorize(Allow.User)]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", Route=Route)]
        HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

    [Function("Node_Admin")]
    [Authorize(Allow.Admin)]
    public Task<HttpResponseData> Admin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "PATCH", "POST", "DELETE", Route=Route)]
        HttpRequestData req)
        => req.Method switch {
            "PATCH" => Patch(req),
            "POST" => Post(req),
            "DELETE" => Delete(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "node get");
        }

        var search = request.OkV;
        if (search.MachineId is Guid machineId) {
            var node = await _context.NodeOperations.GetByMachineId(machineId);
            if (node is null) {
                return await _context.RequestHandling.NotOk(
                    req,
                    Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                    context: machineId.ToString());
            }

            var (tasks, messages) = await (
                _context.NodeTasksOperations.GetByMachineId(machineId).ToListAsync().AsTask(),
                _context.NodeMessageOperations.GetMessages(machineId).ToListAsync().AsTask());

            var commands = messages.Select(m => m.Message).ToList();
            return await RequestHandling.Ok(req, NodeToNodeSearchResult(node, tasks, commands));
        }

        var nodes = await _context.NodeOperations.SearchStates(
            states: search.State,
            poolName: search.PoolName,
            scalesetId: search.ScalesetId).ToListAsync();

        return await RequestHandling.Ok(req, nodes.Select(x => NodeToNodeSearchResult(x, null, null)));
    }

    private static NodeSearchResult NodeToNodeSearchResult(Service.Node node, List<NodeTasks>? tasks, List<NodeCommand>? messages) {
        return new NodeSearchResult(
            PoolId: node.PoolId,
            PoolName: node.PoolName,
            MachineId: node.MachineId,
            Version: node.Version,
            Heartbeat: node.Heartbeat,
            InitializedAt: node.InitializedAt,
            State: node.State,
            ScalesetId: node.ScalesetId,
            ReimageRequested: node.ReimageRequested,
            DeleteRequested: node.DeleteRequested,
            DebugKeepNode: node.DebugKeepNode,
            Tasks: tasks,
            Messages: messages);
    }

    private async Async.Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "NodeReimage");
        }

        var patch = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(patch.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                context: patch.MachineId.ToString());
        }

        node = await _context.NodeOperations.Stop(node, done: true);
        if (node.DebugKeepNode) {
            var r = await _context.NodeOperations.Replace(node with { DebugKeepNode = false });
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("Failed to replace node {MachineId}", node.MachineId);
            }
        }

        return await RequestHandling.Ok(req, true);
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeUpdate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "NodeUpdate");
        }

        var post = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(post.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                context: post.MachineId.ToString());
        }

        if (post.DebugKeepNode is bool value) {
            node = node with { DebugKeepNode = value };
        }

        var r = await _context.NodeOperations.Replace(node);
        if (!r.IsOk) {
            _log.AddTag("HttpRequest", "POST");
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("Failed to replace node {MachineId}", node.MachineId);
        }
        return await RequestHandling.Ok(req, true);
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "NodeDelete");
        }

        var delete = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(delete.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                context: delete.MachineId.ToString());
        }

        node = await _context.NodeOperations.SetHalt(node);
        if (node.DebugKeepNode) {
            var r = await _context.NodeOperations.Replace(node with { DebugKeepNode = false });
            if (!r.IsOk) {
                _log.AddTag("HttpRequest", "DELETE");
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("Failed to replace node {MachineId}", node.MachineId);
            }
        }


        return await RequestHandling.Ok(req, true);
    }
}
