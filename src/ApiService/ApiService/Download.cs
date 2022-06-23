using System.Web;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class Download {
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Download(IEndpointAuthorization auth, IOnefuzzContext context) {
        _auth = auth;
        _context = context;
    }

    // [Function("Download")]
    public Async.Task<HttpResponseData> Run([HttpTrigger("GET")] HttpRequestData req)
        => _auth.CallIfUser(req, Get);

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var query = HttpUtility.ParseQueryString(req.Url.Query);

        var container = query["container"];
        if (container is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'container' query parameter must be provided" }),
                "download");
        }

        var filename = query["filename"];
        if (filename is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new string[] { "'filename' query parameter must be provided" }),
                "download");
        }

        var sasUri = await _context.Containers.GetFileSasUrl(
            new Container(container),
            filename,
            StorageType.Corpus,
            BlobSasPermissions.Read,
            TimeSpan.FromMinutes(5));

        return RequestHandling.Redirect(req, sasUri);
    }
}
