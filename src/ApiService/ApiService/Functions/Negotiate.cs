﻿using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Negotiate {
    private readonly IEndpointAuthorization _auth;
    public Negotiate(IEndpointAuthorization auth) {
        _auth = auth;
    }

    [Function("Negotiate")]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "dashboard")] string info)
        => _auth.CallIfUser(req, r => r.Method switch {
            "POST" => Post(r, info),
            var m => throw new InvalidOperationException($"Unsupported HTTP method {m}"),
        });

    // This endpoint handles the signalr negotation
    // As we do not differentiate from clients at this time, we pass the Functions runtime
    // provided connection straight to the client
    //
    // For more info:
    // https://docs.microsoft.com/en-us/azure/azure-signalr/signalr-concept-internals

    private static async Task<HttpResponseData> Post(HttpRequestData req, string info) {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(info);
        return resp;
    }
}
