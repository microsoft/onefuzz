using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class NodeFunction {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public NodeFunction(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    private static readonly EntityConverter _entityConverter = new();

    // [Function("Node")
    public Async.Task<HttpResponseData> Run([HttpTrigger("GET", "PATCH", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "PATCH" => Patch(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "pool get");
        }

        var search = request.OkV;
        if (search.MachineId is Guid machineId) {
            var node = await _context.NodeOperations.GetByMachineId(machineId);
            if (node is null) {
                return await _context.RequestHandling.NotOk(
                    req,
                    new Error(
                        Code: ErrorCode.UNABLE_TO_FIND,
                        Errors: new string[] { "unable to find node " }),
                    context: machineId.ToString());
            }

            var (tasks, messages) = await (
                _context.NodeTasksOperations.GetByMachineId(machineId).ToListAsync().AsTask(),
                _context.NodeMessageOperations.GetMessage(machineId).ToListAsync().AsTask());

            var commands = messages.Select(m => m.Message).ToList();
            return await RequestHandling.Ok(req, NodeToNodeSearchResult(node with { Tasks = tasks, Messages = commands }));
        }

        var nodes = await _context.NodeOperations.SearchStates(
            states: search.State,
            poolName: search.PoolName,
            scaleSetId: search.ScalesetId).ToListAsync();

        return await RequestHandling.Ok(req, nodes.Select(NodeToNodeSearchResult));
    }

    private static NodeSearchResult NodeToNodeSearchResult(Node node) {
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
            DebugKeepNode: node.DebugKeepNode);
    }

    private async Async.Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "NodeReimage");
        }

        var authCheck = await _auth.CheckRequireAdmins(req);
        if (!authCheck.IsOk) {
            return await _context.RequestHandling.NotOk(req, authCheck.ErrorV, "NodeReimage");
        }

        var patch = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(patch.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_FIND,
                    Errors: new string[] { "unable to find node " }),
                context: patch.MachineId.ToString());
        }

        await _context.NodeOperations.Stop(node, done: true);
        if (node.DebugKeepNode) {
            await _context.NodeOperations.Replace(node with { DebugKeepNode = false });
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

        var authCheck = await _auth.CheckRequireAdmins(req);
        if (!authCheck.IsOk) {
            return await _context.RequestHandling.NotOk(req, authCheck.ErrorV, "NodeUpdate");
        }

        var post = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(post.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_FIND,
                    Errors: new string[] { "unable to find node " }),
                context: post.MachineId.ToString());
        }

        if (post.DebugKeepNode is bool value) {
            node = node with { DebugKeepNode = value };
        }

        await _context.NodeOperations.Replace(node);
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

        var authCheck = await _auth.CheckRequireAdmins(req);
        if (!authCheck.IsOk) {
            return await _context.RequestHandling.NotOk(req, authCheck.ErrorV, "NodeDelete");
        }

        var delete = request.OkV;
        var node = await _context.NodeOperations.GetByMachineId(delete.MachineId);
        if (node is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_FIND,
                    new string[] { "unable to find node" }),
                context: delete.MachineId.ToString());
        }

        await _context.NodeOperations.SetHalt(node);
        if (node.DebugKeepNode) {
            await _context.NodeOperations.Replace(node with { DebugKeepNode = false });
        }

        return await RequestHandling.Ok(req, true);
    }
}
