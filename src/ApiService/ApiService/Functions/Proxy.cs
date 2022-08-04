using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using VmProxy = Microsoft.OneFuzz.Service.Proxy;

namespace Microsoft.OneFuzz.Service.Functions;

public class Proxy {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Proxy(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("proxy")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "PATCH", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "PATCH" => Patch(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }


    private ProxyGetResult GetResult(ProxyForward proxyForward, VmProxy? proxy) {
        var forward = _context.ProxyForwardOperations.ToForward(proxyForward);

        if (proxy == null
            || (proxy.State != VmState.Running && proxy.State != VmState.ExtensionsLaunch)
            || proxy.Heartbeat == null
            || !proxy.Heartbeat.Forwards.Contains(forward)
           ) {
            return new ProxyGetResult(null, Forward: forward);
        }

        return new ProxyGetResult(proxy.Ip, forward);
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ProxyGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "ProxyGet");
        }

        var proxyGet = request.OkV;
        switch ((proxyGet.ScalesetId, proxyGet.MachineId, proxyGet.DstPort)) {
            case (Guid scalesetId, Guid machineId, int dstPort):
                var scaleset = await _context.ScalesetOperations.GetById(scalesetId);
                if (!scaleset.IsOk) {
                    return await _context.RequestHandling.NotOk(req, scaleset.ErrorV, "ProxyGet");
                }

                var proxy = await _context.ProxyOperations.GetOrCreate(scaleset.OkV.Region);
                var forwards = await _context.ProxyForwardOperations.SearchForward(scalesetId: scalesetId,
                    machineId: machineId, dstPort: dstPort).ToListAsync();

                if (!forwards.Any()) {
                    return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "no forwards for scaleset and node" }), "debug_proxy get");
                }

                var response = req.CreateResponse();
                await response.WriteAsJsonAsync(GetResult(forwards[0], proxy));
                return response;

            case (null, null, null):
                var proxies = await _context.ProxyOperations.SearchAll()
                    .Select(p => new ProxyInfo(p.Region, p.ProxyId, p.State)).ToListAsync();

                var r = req.CreateResponse();
                await r.WriteAsJsonAsync(new ProxyList(proxies));
                return r;
            default:
                return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "ProxyGet must provide all or none of the following: scaleset_id, machine_id, dst_port" }), "debug_proxy get");
        }
    }

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ProxyCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "ProxyCreate");
        }

        var scaleset = await _context.ScalesetOperations.GetById(request.OkV.MachineId);
        if (!scaleset.IsOk) {
            return await _context.RequestHandling.NotOk(req, scaleset.ErrorV, "debug_proxy create");
        }

        var forwardResult = await _context.ProxyForwardOperations.UpdateOrCreate(
            region: scaleset.OkV.Region,
            scalesetId: scaleset.OkV.ScalesetId,
            machineId: request.OkV.MachineId,
            dstPort: request.OkV.DstPort,
            duration: request.OkV.Duration
        );

        if (!forwardResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, forwardResult.ErrorV, "debug_proxy create");
        }

        var proxy = await _context.ProxyOperations.GetOrCreate(scaleset.OkV.Region);
        if (proxy != null) {
            var updated = forwardResult.OkV with { ProxyId = proxy.ProxyId };
            await _context.ProxyForwardOperations.Update(updated);
            await _context.ProxyOperations.SaveProxyConfig(proxy);
        }

        var response = req.CreateResponse();
        var result = GetResult(forwardResult.OkV, proxy);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    private async Async.Task<HttpResponseData> Patch(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ProxyReset>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "ProxyReset");
        }

        var proxyList = await _context.ProxyOperations.SearchByPartitionKeys(new[] { $"{request.OkV.Region}" }).ToListAsync();

        foreach (var proxy in proxyList) {
            await _context.ProxyOperations.SetState(proxy, VmState.Stopping);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new BoolResult(proxyList.Any()));
        return response;
    }


    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<ProxyDelete>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "debug_proxy delet");
        }

        var regions = await _context.ProxyForwardOperations.RemoveForward(
            scalesetId: request.OkV.ScalesetId,
            machineId: request.OkV.MachineId,
            dstPort: request.OkV.DstPort
        );

        foreach (var region in regions) {
            var proxy = await _context.ProxyOperations.GetOrCreate(region);
            if (proxy != null) {
                await _context.ProxyOperations.SaveProxyConfig(proxy);
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new BoolResult(true));
        return response;
    }
}
