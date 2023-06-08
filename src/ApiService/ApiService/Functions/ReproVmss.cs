using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class ReproVmss {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public ReproVmss(ILogger<ReproVmss> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("ReproVms")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE", Route = "repro_vms")]
        HttpRequestData req,
        FunctionContext context)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req, context),
            "DELETE" => Delete(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

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
            var auth = await _context.SecretsOperations.GetSecretValue<Authentication>(vm.Auth);

            if (auth == null) {
                return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "no auth info for the VM"), $"{request.OkV.VmId}");
            }
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(ReproVmResponse.FromRepro(vm, auth));
            return response;
        }

        var vms = _context.ReproOperations.SearchStates(VmStateHelper.Available);
        var response2 = req.CreateResponse(HttpStatusCode.OK);
        await response2.WriteAsJsonAsync(vms);
        return response2;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req, FunctionContext context) {
        var request = await RequestHandling.ParseRequest<ReproCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "repro_vm create");
        }

        var userInfo = context.GetUserAuthInfo();

        var create = request.OkV;
        var cfg = new ReproConfig(
            Container: create.Container,
            Path: create.Path,
            Duration: create.Duration);

        var vm = await _context.ReproOperations.Create(cfg, userInfo.UserInfo);
        if (!vm.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                vm.ErrorV,
                "repro_vm create");
        }

        var auth = await _context.SecretsOperations.GetSecretValue<Authentication>(vm.OkV.Auth);
        if (auth is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.INVALID_REQUEST, "unable to find auth"),
                "repro_vm create");
        }

        // we’d like to track the usage of this feature;
        // anonymize the user ID so we can distinguish multiple requests
        {
            var data = userInfo.UserInfo.ToString(); // rely on record ToString
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
            _log.AddTag("UserHash", hash);
            _log.LogEvent("created repro VM");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(ReproVmResponse.FromRepro(vm.OkV, auth));
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
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("Failed to replace repro {VmId}", updatedRepro.VmId);
        }

        if (vm.Auth != null) {
            await _context.SecretsOperations.DeleteSecret(vm.Auth);
        }
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(ReproVmResponse.FromRepro(vm, new Authentication("", "", "")));
        return response;
    }
}
