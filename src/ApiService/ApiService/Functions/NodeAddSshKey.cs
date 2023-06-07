using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class NodeAddSshKey {
    private readonly IOnefuzzContext _context;

    public NodeAddSshKey(IOnefuzzContext context) {
        _context = context;
    }

    [Function("NodeAddSshKey")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "node/add_ssh_key")] HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeAddSshKeyPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "NodeAddSshKey");
        }

        var node = await _context.NodeOperations.GetByMachineId(request.OkV.MachineId);

        if (node == null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                $"{request.OkV.MachineId}");
        }

        var result = await _context.NodeOperations.AddSshPublicKey(node, request.OkV.PublicKey);
        if (!result.IsOk) {
            return await _context.RequestHandling.NotOk(req, result.ErrorV, "NodeAddSshKey");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new BoolResult(true));
        return response;
    }
}
