using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Tools {
    private readonly IOnefuzzContext _context;

    public Tools(IOnefuzzContext context) {
        _context = context;
    }

    [Function("Tools")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req) {

        //Note: streaming response are not currently supported by in isolated functions
        // https://github.com/Azure/azure-functions-dotnet-worker/issues/958
        var response = req.CreateResponse(HttpStatusCode.OK);
        var downloadResult = await _context.Containers.DownloadAsZip(WellKnownContainers.Tools, StorageType.Config, response.Body);
        if (!downloadResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, downloadResult.ErrorV, "download tools");
        }
        return response;
    }
}
