using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;


class Proxy : IFromJsonElement<Proxy> {

    JsonElement _e;
    public Proxy() { }
    public Proxy(JsonElement e) => _e = e;

    public string Region => _e.GetProperty("region").GetString()!;
    public Guid ProxyId => _e.GetProperty("proxy_id").GetGuid()!;

    public string VmState => _e.GetProperty("state").GetString()!;

    public Proxy Convert(JsonElement e) => new Proxy(e);
}


class Forward : IFromJsonElement<Forward> {
    JsonElement _e;
    public Forward() { }
    public Forward(JsonElement e) => _e = e;

    public long SrcPort => _e.GetProperty("src_port").GetInt64();
    public long DstPort => _e.GetProperty("dst_port").GetInt64();

    public string DstIp => _e.GetProperty("dst_ip").GetString()!;

    public Forward Convert(JsonElement e) => new Forward(e);
}

class ProxyGetResult : IFromJsonElement<ProxyGetResult> {
    JsonElement _e;

    public ProxyGetResult() { }

    public ProxyGetResult(JsonElement e) => _e = e;

    public string? Ip => _e.ValueKind == JsonValueKind.Null ? null : _e.GetProperty("ip").GetString();

    public Forward Forward => new Forward(_e.GetProperty("forward"));

    public ProxyGetResult Convert(JsonElement e) => new ProxyGetResult(e);
}


class ProxyApi : ApiBase {

    public ProxyApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/proxy", request, output) {
    }

    public async Task<Result<IEnumerable<Proxy>, Error>> Get(Guid? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);

        var r = await Get(root);
        if (Error.IsError(r)) {
            return Result<IEnumerable<Proxy>, Error>.Error(new Error(r));
        } else {
            return IEnumerableResult<Proxy>(r.GetProperty("proxies"));
        }
    }

    public async Task<BooleanResult> Delete(Guid? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);
        return DeleteResult<BooleanResult>(await Delete(root));
    }

    public async Task<JsonElement> Reset(string region) {
        var root = new JsonObject();
        root.Add("region", region);
        var r = await Patch(root);
        return r;
    }

    public async Task<Result<ProxyGetResult, Error>> Create(Guid scalesetId, Guid machineId, int dstPort, int duration) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        root.Add("dst_port", dstPort);
        root.Add("duration", duration);

        var r = await Post(root);
        return Result<ProxyGetResult>(r);
    }

}
