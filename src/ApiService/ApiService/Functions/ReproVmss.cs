using System.Net;
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

    [Function("repro_vmss")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req) {
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
                return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "no such VM" }), $"{request.OkV.VmId}");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(vm);
            return response;
        }

        var vms = await _context.ReproOperations.SearchStates(VmStateHelper.Available).Select(vm => vm with { Auth = null }).ToListAsync();
        var response2 = req.CreateResponse(HttpStatusCode.OK);
        await response2.WriteAsJsonAsync(vms);
        return response2;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ReproConfig>(req);
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

        var vm = await _context.ReproOperations.Create(request.OkV, userInfo.OkV);
        if (!vm.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                vm.ErrorV,
                "repro_vm create");
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
                new Error(ErrorCode.INVALID_REQUEST, new[] { "missing vm_id" }),
                context: "repro delete");
        }

        var vm = await _context.ReproOperations.SearchByPartitionKeys(new[] { $"request.OkV.VmId" }).FirstOrDefaultAsync();

        if (vm == null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(ErrorCode.INVALID_REQUEST, new[] { "no such vm" }),
                context: "repro delete");
        }

        var updatedRepro = vm with { State = VmState.Stopping };
        await _context.ReproOperations.Replace(updatedRepro);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(updatedRepro);
        return response;
    }
}
