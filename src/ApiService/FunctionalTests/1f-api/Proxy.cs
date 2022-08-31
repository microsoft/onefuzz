using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;
public class Proxy : IFromJsonElement<Proxy> {

    JsonElement _e;
    public Proxy() { }
    public Proxy(JsonElement e) => _e = e;

    public string Region => _e.GetProperty("region").GetString()!;
    public Guid ProxyId => _e.GetProperty("proxy_id").GetGuid()!;

    public string VmState => _e.GetProperty("state").GetString()!;

    public Proxy Convert(JsonElement e) => new Proxy(e);
}

public class Forward : IFromJsonElement<Forward>, IComparable<Forward> {
    JsonElement _e;
    public Forward() { }
    public Forward(JsonElement e) => _e = e;

    public long SrcPort => _e.GetProperty("src_port").GetInt64();
    public long DstPort => _e.GetProperty("dst_port").GetInt64();

    public string DstIp => _e.GetProperty("dst_ip").GetString()!;

    public Forward Convert(JsonElement e) => new Forward(e);

    public int CompareTo(Forward? other) {
        if (other == null) return 1;
        var c = other.DstIp.CompareTo(DstIp);
        if (c != 0) return c;
        c = other.SrcPort.CompareTo(SrcPort);
        if (c != 0) return c;
        c = other.DstPort.CompareTo(DstPort);
        return c;
    }
}

public class ProxyGetResult : IFromJsonElement<ProxyGetResult>, IComparable<ProxyGetResult> {
    JsonElement _e;

    public ProxyGetResult() { }

    public ProxyGetResult(JsonElement e) => _e = e;

    public string? Ip {
        get {
            JsonElement ip;
            if (_e.TryGetProperty("ip", out ip)){
                if (ip.ValueKind == JsonValueKind.Null) {
                    return null;
                } else {
                    return ip.GetString();
                }
            } else {
                return null;
            }
        }
    }

    public Forward Forward => new Forward(_e.GetProperty("forward"));

    public ProxyGetResult Convert(JsonElement e) => new ProxyGetResult(e);

    public int CompareTo(ProxyGetResult? other) {

        if (other is null)
            return 1;

        var c = 0;
        if (other.Ip is not null && Ip is not null) {
            c = other.Ip.CompareTo(Ip);
            if (c != 0) return c;
        } else if (other.Ip is null && Ip is null) {
            c = 0;
        } else {
            return -1;
        }
        c = other.Forward.CompareTo(Forward);
        return c;
    }
}


public class ProxyApi : ApiBase {

    public ProxyApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/proxy", request, output) {
    }

    public async Task<Result<IEnumerable<Proxy>, Error>> Get(Guid? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject();
        if (scalesetId is not null)
            root.Add("scaleset_id", scalesetId);
        if (machineId is not null)
            root.Add("machine_id", machineId);
        if (dstPort is not null)
            root.Add("dst_port", dstPort);

        var r = await Get(root);
        if (Error.IsError(r)) {
            return Result<IEnumerable<Proxy>, Error>.Error(new Error(r));
        } else {
            return IEnumerableResult<Proxy>(r.GetProperty("proxies"));
        }
    }

    public async Task<BooleanResult> Delete(Guid scalesetId, Guid machineId, int? dstPort = null) {
        var root = new JsonObject();
        root.Add("scaleset_id", scalesetId);
        root.Add("machine_id", machineId);
        if (dstPort != null)
            root.Add("dst_port", dstPort);
        return Return<BooleanResult>(await Delete(root));
    }

    public async Task<BooleanResult> Reset(string region) {
        var root = new JsonObject();
        root.Add("region", region);
        var r = await Patch(root);
        return Return<BooleanResult>(r);
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
