using System.Net;
using System.Web;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Download {
    private readonly IOnefuzzContext _context;

    public Download(IOnefuzzContext context) {
        _context = context;
    }

    [Function("Download")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req) {
        var query = HttpUtility.ParseQueryString(req.Url.Query);

        var queryContainer = query["container"];
        if (queryContainer is null || !Container.TryParse(queryContainer, out var container)) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    "'container' query parameter must be provided and valid"),
                "download");
        }

        var filename = query["filename"];
        if (filename is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    "'filename' query parameter must be provided"),
                "download");
        }

        var sasUri = await _context.Containers.GetFileSasUrl(
            container,
            filename,
            StorageType.Corpus,
            BlobSasPermissions.Read,
            TimeSpan.FromMinutes(5));

        if (sasUri is null) {
            // Note that we do not validate the existence of the file, only the container.
            return await _context.RequestHandling.NotOk(req,
                Error.Create(
                    ErrorCode.INVALID_CONTAINER,
                    "container not found"),
                "generating download file SAS",
                HttpStatusCode.NotFound);
        }

        return RequestHandling.Redirect(req, sasUri);
    }
}
