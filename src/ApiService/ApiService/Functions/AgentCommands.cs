using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class AgentCommands {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public AgentCommands(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("AgentCommands")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "DELETE", Route="agents/commands")]
        HttpRequestData req)
        => _auth.CallIfAgent(req, r => r.Method switch {
            "GET" => Get(req),
            "DELETE" => Delete(req),
            _ => throw new NotSupportedException($"HTTP Method {req.Method} is not supported for this method")
        });

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeCommandGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(NodeCommandGet).ToString());
        }
        var nodeCommand = request.OkV;

        var message = await _context.NodeMessageOperations.GetMessage(nodeCommand.MachineId).FirstOrDefaultAsync();
        if (message != null) {
            var command = message.Message;
            var messageId = message.MessageId;
            var envelope = new NodeCommandEnvelope(command, messageId);
            return await RequestHandling.Ok(req, new PendingNodeCommand(envelope));
        } else {
            return await RequestHandling.Ok(req, new PendingNodeCommand(null));
        }
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeCommandDelete>(req);
        if (!request.IsOk || request.OkV == null) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(NodeCommandDelete).ToString());
        }
        var nodeCommand = request.OkV;

        var message = await _context.NodeMessageOperations.GetEntityAsync(nodeCommand.MachineId.ToString(), nodeCommand.MessageId);
        if (message != null) {
            await _context.NodeMessageOperations.Delete(message);
        }

        return await RequestHandling.Ok(req, new BoolResult(true));
    }
}
