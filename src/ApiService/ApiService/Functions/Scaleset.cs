using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Scaleset {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Scaleset(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("Scaleset")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.User, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req)
        => req.Method switch {
            "GET" => Get(req),
            "PATCH" => Patch(req),
            "POST" => Post(req),
            "DELETE" => Delete(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

    private async Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetStop>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetDelete");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "ScalesetDelete");
        }

        var scalesetResult = await _context.ScalesetOperations.GetById(request.OkV.ScalesetId);
        if (!scalesetResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetDelete");
        }

        var scaleset = scalesetResult.OkV;
        // result ignored: not used after this point
        _ = await _context.ScalesetOperations.SetShutdown(scaleset, request.OkV.Now);
        return await RequestHandling.Ok(req, true);
    }

    private async Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetCreate");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "ScalesetCreate");
        }

        var create = request.OkV;
        // verify the pool exists
        var poolResult = await _context.PoolOperations.GetByName(create.PoolName);
        if (!poolResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, poolResult.ErrorV, "ScalesetCreate");
        }

        var pool = poolResult.OkV;
        if (!pool.Managed) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_CREATE, "scalesets can only be added to managed pools "),
                context: "ScalesetCreate");
        }

        ImageReference image;
        if (create.Image is null) {
            var config = await _context.ConfigOperations.Fetch();
            if (pool.Os == Os.Windows) {
                image = config.DefaultWindowsVmImage ?? DefaultImages.Windows;
            } else {
                image = config.DefaultLinuxVmImage ?? DefaultImages.Linux;
            }
        } else {
            image = create.Image;
        }

        Region region;
        if (create.Region is null) {
            region = await _context.Creds.GetBaseRegion();
        } else {
            var validRegions = await _context.Creds.GetRegions();
            if (!validRegions.Contains(create.Region)) {
                return await _context.RequestHandling.NotOk(
                    req,
                    Error.Create(ErrorCode.UNABLE_TO_CREATE, "invalid region"),
                    context: "ScalesetCreate");
            }

            region = create.Region;
        }

        var availableSkus = await _context.VmssOperations.ListAvailableSkus(region);
        if (!availableSkus.Contains(create.VmSku)) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_CREATE, $"The specified VM SKU '{create.VmSku}' is not available in the location ${region}"),
                context: "ScalesetCreate");
        }

        var tags = create.Tags ?? new Dictionary<string, string>();
        var configTags = (await _context.ConfigOperations.Fetch()).VmssTags;
        if (configTags is not null) {
            foreach (var (key, value) in configTags) {
                tags[key] = value;
            }
        }

        var scaleset = new Service.Scaleset(
            ScalesetId: Service.Scaleset.GenerateNewScalesetId(create.PoolName),
            State: ScalesetState.Init,
            NeedsConfigUpdate: false,
            Auth: await AuthHelpers.BuildAuth(_log),
            PoolName: create.PoolName,
            VmSku: create.VmSku,
            Image: image,
            Region: region,
            Size: create.Size,
            SpotInstances: create.SpotInstances,
            EphemeralOsDisks: create.EphemeralOsDisks,
            Tags: tags);

        var inserted = await _context.ScalesetOperations.Insert(scaleset);
        if (!inserted.IsOk) {
            _log.WithHttpStatus(inserted.ErrorV).Error($"failed to insert new scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"unable to insert scaleset: {inserted.ErrorV}"
                ),
                context: "ScalesetCreate");
        }

        if (create.AutoScale is AutoScaleOptions options) {
            var autoScale = new AutoScale(
                scaleset.ScalesetId,
                Min: options.Min,
                Max: options.Max,
                Default: options.Default,
                ScaleOutAmount: options.ScaleOutAmount,
                ScaleOutCooldown: options.ScaleOutCooldown,
                ScaleInAmount: options.ScaleInAmount,
                ScaleInCooldown: options.ScaleInCooldown);

            var r = await _context.AutoScaleOperations.Insert(autoScale);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"failed to insert autoscale options for sclaeset id {autoScale.ScalesetId:Tag:ScalesetId}");
            }
        }

        // auth not included on create results, only GET with include_auth set
        var response = ScalesetResponse.ForScaleset(scaleset, includeAuth: false);
        return await RequestHandling.Ok(req, response);
    }

    private async Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetUpdate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetUpdate");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "ScalesetUpdate");
        }

        var scalesetResult = await _context.ScalesetOperations.GetById(request.OkV.ScalesetId);
        if (!scalesetResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetUpdate");
        }

        var scaleset = scalesetResult.OkV;
        if (!scaleset.State.CanUpdate()) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_REQUEST,
                    $"scaleset must be in one of the following states to update: {string.Join(", ", ScalesetStateHelper.CanUpdateStates)}"),
                "ScalesetUpdate");
        }

        if (request.OkV.Size is long size) {
            scaleset = await _context.ScalesetOperations.SetSize(scaleset, size);
        }

        var response = ScalesetResponse.ForScaleset(scaleset, includeAuth: false);
        return await RequestHandling.Ok(req, response);
    }

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetSearch");
        }

        var search = request.OkV;
        if (search.ScalesetId is ScalesetId id) {
            var scalesetResult = await _context.ScalesetOperations.GetById(id);
            if (!scalesetResult.IsOk) {
                return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetSearch");
            }

            var scaleset = scalesetResult.OkV;

            var response = ScalesetResponse.ForScaleset(scaleset, includeAuth: search.IncludeAuth);
            response = response with { Nodes = await _context.ScalesetOperations.GetNodes(scaleset) };
            return await RequestHandling.Ok(req, response);
        }

        var states = search.State ?? Enumerable.Empty<ScalesetState>();
        var scalesets = await _context.ScalesetOperations.SearchStates(states).ToListAsync();
        // don't return auths during list actions, only 'get'
        var result = scalesets.Select(ss => ScalesetResponse.ForScaleset(ss, includeAuth: false));
        return await RequestHandling.Ok(req, result);
    }
}
