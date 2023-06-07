using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class AgentCommands {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public AgentCommands(ILogger<AgentCommands> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("AgentCommands")]
    [Authorize(Allow.Agent)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "DELETE", Route="agents/commands")]
        HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "DELETE" => Delete(req),
            _ => throw new NotSupportedException($"HTTP Method {req.Method} is not supported for this method")
        };

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeCommandGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(NodeCommandGet).ToString());
        }
        var nodeCommand = request.OkV;

        var message = await _context.NodeMessageOperations.GetMessage(nodeCommand.MachineId);
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
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(NodeCommandDelete).ToString());
        }
        var nodeCommand = request.OkV;

        var message = await _context.NodeMessageOperations.GetEntityAsync(nodeCommand.MachineId.ToString(), nodeCommand.MessageId);
        if (message != null) {
            await _context.NodeMessageOperations.Delete(message).IgnoreResult();
        } else {
            _log.AddTag("HttpRequest", "DELETE");
            _log.LogDebug("failed to find {MachineId} for {MessageId}", nodeCommand.MachineId, nodeCommand.MessageId);
        }
        return await RequestHandling.Ok(req, new BoolResult(true));
    }
}
