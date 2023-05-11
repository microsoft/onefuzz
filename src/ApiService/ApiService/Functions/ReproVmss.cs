using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class ReproVmss {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public ReproVmss(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("ReproVms")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE", Route = "repro_vms")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ReproGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "repro_vm get");
        }

        if (request.OkV.VmId != null) {
            var vm = await _context.ReproOperations.SearchByPartitionKeys(new[] { $"{request.OkV.VmId}" }).FirstOrDefaultAsync();

            if (vm == null) {
                return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "no such VM"), $"{request.OkV.VmId}");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(vm);
            return response;
        }

        var vms = _context.ReproOperations.SearchStates(VmStateHelper.Available).Select(vm => vm with { Auth = null });
        var response2 = req.CreateResponse(HttpStatusCode.OK);
        await response2.WriteAsJsonAsync(vms);
        return response2;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ReproCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "repro_vm create");
        }

        var userInfo = await _context.UserCredentials.ParseJwtToken(req);
        if (!userInfo.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                userInfo.ErrorV,
                "repro_vm create");
        }

        var create = request.OkV;
        var cfg = new ReproConfig(
            Container: create.Container,
            Path: create.Path,
            Duration: create.Duration);

        var vm = await _context.ReproOperations.Create(cfg, userInfo.OkV.UserInfo);
        if (!vm.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                vm.ErrorV,
                "repro_vm create");
        }

        // we’d like to track the usage of this feature; 
        // anonymize the user ID so we can distinguish multiple requests
        {
            var data = userInfo.OkV.UserInfo.ToString(); // rely on record ToString
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
            _log.Event($"created repro VM, user distinguisher: {hash:Tag:UserHash}");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(vm.OkV);
        return response;
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ReproGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "NodeDelete");
        }

        if (request.OkV.VmId == null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.INVALID_REQUEST, "missing vm_id"),
                context: "repro delete");
        }

        var vm = await _context.ReproOperations.SearchByPartitionKeys(new[] { request.OkV.VmId.ToString()! }).FirstOrDefaultAsync();

        if (vm == null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.INVALID_REQUEST, "no such vm"),
                context: "repro delete");
        }

        var updatedRepro = vm with { State = VmState.Stopping };
        var r = await _context.ReproOperations.Replace(updatedRepro);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"Failed to replace repro {updatedRepro.VmId:Tag:VmId}");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(updatedRepro);
        return response;
    }
}
