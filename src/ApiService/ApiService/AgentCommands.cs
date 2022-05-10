using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class AgentCommands {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public AgentCommands(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    // [Function("AgentCommands")]
    public async Async.Task<HttpResponseData> Run([HttpTrigger("get", "delete")] HttpRequestData req) {
        return req.Method switch {
            "GET" => await Get(req),
            "DELETE" => await Delete(req),
            _ => throw new NotImplementedException($"HTTP Method {req.Method} is not supported for this method")
        };
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeCommandGet>(req);
        if (!request.IsOk || request.OkV == null) {
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
