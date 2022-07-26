﻿using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class NodeAddSshKey {

    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public NodeAddSshKey(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
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
                new Error(ErrorCode.UNABLE_TO_FIND, new[] { "unable to find node" }),
                $"{request.OkV.MachineId}");
        }

        var result = await _context.NodeOperations.AddSshPublicKey(node, request.OkV.PublicKey);
        if (!result.IsOk) {
            return await _context.RequestHandling.NotOk(req, result.ErrorV, "NodeAddSshKey");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;


    }

    [Function("node")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "node/add_ssh_key")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            // "POST" => Post(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

}
