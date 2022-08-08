using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class ContainersFunction {
    private readonly ILogTracer _logger;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public ContainersFunction(ILogTracer logger, IEndpointAuthorization auth, IOnefuzzContext context) {
        _logger = logger;
        _auth = auth;
        _context = context;
    }

    [Function("Containers")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new NotSupportedException(),
        });

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {

        // see if one particular container is specified:
        if (req.Body.Length > 0) {
            var request = await RequestHandling.ParseRequest<ContainerGet>(req);
            if (!request.IsOk) {
                return await _context.RequestHandling.NotOk(req, request.ErrorV, "container get");
            }

            var get = request.OkV;

            var container = await _context.Containers.FindContainer(get.Name, StorageType.Corpus);
            if (container is null) {
                return await _context.RequestHandling.NotOk(
                    req,
                    new Error(
                        Code: ErrorCode.INVALID_REQUEST,
                        Errors: new[] { "invalid container" }),
                    context: get.Name.ContainerName);
            }

            var metadata = (await container.GetPropertiesAsync()).Value.Metadata;

            var sas = await _context.Containers.GetContainerSasUrl(
                get.Name,
                StorageType.Corpus,
                BlobContainerSasPermissions.Read
                | BlobContainerSasPermissions.Write
                | BlobContainerSasPermissions.Delete
                | BlobContainerSasPermissions.List);

            return await RequestHandling.Ok(req, new ContainerInfo(
                Name: get.Name,
                SasUrl: sas,
                Metadata: metadata));
        }

        // otherwise list all containers
        var containers = await _context.Containers.GetContainers(StorageType.Corpus);
        var result = containers.Select(c => new ContainerInfoBase(new Container(c.Key), c.Value));
        return await RequestHandling.Ok(req, result);
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ContainerDelete>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "container delete");
        }

        var delete = request.OkV;
        _logger.Info($"container - deleting {delete.Name}");
        var container = await _context.Containers.FindContainer(delete.Name, StorageType.Corpus);

        var deleted = false;
        if (container is not null) {
            deleted = await container.DeleteIfExistsAsync();
        }

        return await RequestHandling.Ok(req, deleted);
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ContainerCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "container create");
        }

        var post = request.OkV;
        _logger.Info($"container - creating {post.Name}");
        var sas = await _context.Containers.CreateContainer(
            post.Name,
            StorageType.Corpus,
            post.Metadata);

        if (sas is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.INVALID_REQUEST,
                    Errors: new[] { "invalid container" }),
                context: post.Name.ContainerName);
        }

        return await RequestHandling.Ok(
            req,
            new ContainerInfo(
                Name: post.Name,
                SasUrl: sas,
                Metadata: post.Metadata));
    }
}
