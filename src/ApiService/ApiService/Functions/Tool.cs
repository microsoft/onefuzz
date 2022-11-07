using System.IO;
using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Tools {
    private readonly IOnefuzzContext _context;
    private readonly IEndpointAuthorization _auth;

    public Tools(IEndpointAuthorization auth, IOnefuzzContext context) {
        _context = context;
        _auth = auth;
    }

    private static string ReadResource(Assembly asm, string resourceName) {
        using var r = asm.GetManifestResourceStream(resourceName);
        if (r is null) {
            return "unknown";
        }

        using var sr = new StreamReader(r);
        return sr.ReadToEnd().Trim();
    }

    public async Async.Task<HttpResponseData> GetResponse(HttpRequestData req) {
        //Note: streaming response are not currently supported by in isolated functions
        // https://github.com/Azure/azure-functions-dotnet-worker/issues/958
        var response = req.CreateResponse(HttpStatusCode.OK);
        var downloadResult = await _context.Containers.DownloadAsZip(WellKnownContainers.Tools, StorageType.Config, response.Body);
        if (!downloadResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, downloadResult.ErrorV, "download tools");
        }
        return response;
    }


    [Function("Tools")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req)
        => _auth.CallIfUser(req, GetResponse);
}
