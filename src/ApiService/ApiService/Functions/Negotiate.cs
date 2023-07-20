using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Negotiate {
    [Function("Negotiate")]
    [Authorize(Allow.User)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "dashboard")] string info) {

        // This endpoint handles the signalr negotation
        // As we do not differentiate from clients at this time, we pass the Functions runtime
        // provided connection straight to the client
        //
        // For more info:
        // https://docs.microsoft.com/en-us/azure/azure-signalr/signalr-concept-internals

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(info);
        return resp;
    }
}
