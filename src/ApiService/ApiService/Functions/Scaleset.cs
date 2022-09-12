using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

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
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "PATCH" => Patch(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

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
        await _context.ScalesetOperations.SetShutdown(scaleset, request.OkV.Now);
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
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "ScalesetCreate");
        }

        var pool = poolResult.OkV;
        if (!pool.Managed) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_CREATE,
                    Errors: new string[] { "scalesets can only be added to managed pools " }),
                context: "ScalesetCreate");
        }

        Region region;
        if (create.Region is null) {
            region = await _context.Creds.GetBaseRegion();
        } else {
            var validRegions = await _context.Creds.GetRegions();
            if (!validRegions.Contains(create.Region)) {
                return await _context.RequestHandling.NotOk(
                    req,
                    new Error(
                        Code: ErrorCode.UNABLE_TO_CREATE,
                        Errors: new string[] { "invalid region" }),
                    context: "ScalesetCreate");
            }

            region = create.Region;
        }

        var availableSkus = await _context.VmssOperations.ListAvailableSkus(region);
        if (!availableSkus.Contains(create.VmSku)) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_CREATE,
                    Errors: new string[] { $"The specified VM SKU '{create.VmSku}' is not available in the location ${region}" }),
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
            ScalesetId: Guid.NewGuid(),
            State: ScalesetState.Init,
            NeedsConfigUpdate: false,
            Auth: Auth.BuildAuth(),
            PoolName: create.PoolName,
            VmSku: create.VmSku,
            Image: create.Image,
            Region: region,
            Size: create.Size,
            SpotInstances: create.SpotInstances,
            EphemeralOsDisks: create.EphemeralOsDisks,
            Tags: tags);

        var inserted = await _context.ScalesetOperations.Insert(scaleset);
        if (!inserted.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_CREATE,
                    new string[] { $"unable to insert scaleset: {inserted.ErrorV}" }
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

            await _context.AutoScaleOperations.Insert(autoScale);
        }

        return await RequestHandling.Ok(req, ScalesetResponse.ForScaleset(scaleset));
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
                new Error(
                    Code: ErrorCode.INVALID_REQUEST,
                    Errors: new[] { $"scaleset must be in one of the following states to update: {string.Join(", ", ScalesetStateHelper.CanUpdateStates)}" }),
                "ScalesetUpdate");
        }

        if (request.OkV.Size is long size) {
            scaleset = await _context.ScalesetOperations.SetSize(scaleset, size);
        }

        scaleset = scaleset with { Auth = null };
        return await RequestHandling.Ok(req, ScalesetResponse.ForScaleset(scaleset));
    }

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ScalesetSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ScalesetSearch");
        }

        var search = request.OkV;
        if (search.ScalesetId is Guid id) {
            var scalesetResult = await _context.ScalesetOperations.GetById(id);
            if (!scalesetResult.IsOk) {
                return await _context.RequestHandling.NotOk(req, scalesetResult.ErrorV, "ScalesetSearch");
            }

            var scaleset = scalesetResult.OkV;

            var response = ScalesetResponse.ForScaleset(scaleset);
            response = response with { Nodes = await _context.ScalesetOperations.GetNodes(scaleset) };
            if (!search.IncludeAuth) {
                response = response with { Auth = null };
            }

            return await RequestHandling.Ok(req, response);
        }

        var states = search.State ?? Enumerable.Empty<ScalesetState>();
        var scalesets = await _context.ScalesetOperations.SearchStates(states).ToListAsync();
        // don't return auths during list actions, only 'get'
        var result = scalesets.Select(ss => ScalesetResponse.ForScaleset(ss with { Auth = null }));
        return await RequestHandling.Ok(req, result);
    }
}
