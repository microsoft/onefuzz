using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;


class Proxy {

    JsonElement _e;
    public Proxy() { }
    public Proxy(JsonElement e) => _e = e;

    public string Region => _e.GetProperty("region").GetString()!;
    public Guid ProxyId => _e.GetProperty("proxy_id").GetGuid()!;

    public string VmState => _e.GetProperty("state").GetString()!;


}

class ProxyApi : ApiBase<Proxy> {

    public ProxyApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/proxy", request, output) {
    }

    public override Proxy Convert(JsonElement e) => new Proxy(e);

    public async Task<Result<IEnumerable<Proxy>, Error>> Get(Guid? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);

        var r = await Get(root);
        if (Error.IsError(r)) {
            return Result<IEnumerable<Proxy>, Error>.Error(new Error(r));
        } else {
            return IEnumerableResult(r.GetProperty("proxies"));
        }
    }

    public async Task<bool> Delete(Guid? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);
        return DeleteResult(await Delete(root));
    }

    public async Task<JsonElement> Reset(string region) {
        var root = new JsonObject();
        root.Add("region", region);
        var r = await Patch(root);
        return r;
    }

    public async Task<Result<Proxy, Error>> Create(Guid scalesetId, Guid machineId, int dstPort, int duration) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);
        root.Add("duration", duration);

        var r = await Post(root);
        return Result(r);
    }

}
