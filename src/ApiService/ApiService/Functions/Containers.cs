using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class ContainersFunction {
    private readonly ILogger _logger;
    private readonly IOnefuzzContext _context;

    public ContainersFunction(ILogger<ContainersFunction> logger, IOnefuzzContext context) {
        _logger = logger;
        _context = context;
    }

    [Function("Containers")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "PUT", "DELETE")] HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req),
            "DELETE" => Delete(req),
            "PUT" => Put(req),
            _ => throw new NotSupportedException(),
        };

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
                    Error.Create(
                        ErrorCode.INVALID_REQUEST,
                        $"invalid container '{get.Name}'"),
                    context: get.Name.String);
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
        var result = containers.Select(c => new ContainerInfoBase(c.Key, c.Value));
        return await RequestHandling.Ok(req, result);
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ContainerDelete>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "container delete");
        }

        var delete = request.OkV;
        _logger.LogInformation("deleting {ContainerName}", delete.Name);
        var deleted = await _context.Containers.DeleteContainerIfExists(delete.Name, StorageType.Corpus);
        return await RequestHandling.Ok(req, deleted);
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ContainerCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "container create");
        }

        var post = request.OkV;
        _logger.LogInformation("creating {ContainerName}", post.Name);
        var sas = await _context.Containers.GetOrCreateNewContainer(
            post.Name,
            StorageType.Corpus,
            post.Metadata);

        if (sas is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    "invalid container"),
                context: post.Name.String);
        }

        return await RequestHandling.Ok(
            req,
            new ContainerInfo(
                Name: post.Name,
                SasUrl: sas,
                Metadata: post.Metadata));
    }

    private async Async.Task<HttpResponseData> Put(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ContainerUpdate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "container update");
        }

        var toUpdate = request.OkV;
        _logger.LogInformation("updating {ContainerName}", toUpdate.Name);
        var updated = await _context.Containers.CreateOrUpdateContainerTag(toUpdate.Name, StorageType.Corpus, toUpdate.Metadata.ToDictionary(x => x.Key, x => x.Value));

        if (!updated.IsOk) {
            return await _context.RequestHandling.NotOk(req, updated.ErrorV, "container update");
        }

        return await RequestHandling.Ok(req, new ContainerInfoBase(toUpdate.Name, toUpdate.Metadata));
    }
}
