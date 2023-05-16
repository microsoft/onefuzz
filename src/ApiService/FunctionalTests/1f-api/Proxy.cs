using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;
public class Proxy : IFromJsonElement<Proxy> {
    readonly JsonElement _e;
    public Proxy() { }
    public Proxy(JsonElement e) => _e = e;

    public string Region => _e.GetStringProperty("region");
    public Guid ProxyId => _e.GetGuidProperty("proxy_id");

    public string VmState => _e.GetStringProperty("state");

    public static Proxy Convert(JsonElement e) => new(e);
}

public class Forward : IFromJsonElement<Forward>, IComparable<Forward> {
    readonly JsonElement _e;
    public Forward() { }
    public Forward(JsonElement e) => _e = e;

    public long SrcPort => _e.GetLongProperty("src_port");
    public long DstPort => _e.GetLongProperty("dst_port");

    public string DstIp => _e.GetStringProperty("dst_ip");

    public static Forward Convert(JsonElement e) => new(e);

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
    readonly JsonElement _e;

    public ProxyGetResult() { }

    public ProxyGetResult(JsonElement e) => _e = e;

    public string? Ip {
        get {
            JsonElement ip;
            if (_e.TryGetProperty("ip", out ip)) {
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

    public Forward Forward => new(_e.GetProperty("forward"));

    public static ProxyGetResult Convert(JsonElement e) => new(e);

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

    public async Task<Result<IEnumerable<Proxy>, Error>> Get(string? scalesetId = null, Guid? machineId = null, int? dstPort = null) {
        var root = new JsonObject()
            .AddIfNotNullV("scaleset_id", scalesetId)
            .AddIfNotNullV("machine_id", machineId)
            .AddIfNotNullV("dst_port", dstPort);

        var r = await Get(root);
        return IEnumerableResult<Proxy>(r.GetProperty("proxies"));
    }

    public async Task<BooleanResult> Delete(string scalesetId, Guid machineId, int? dstPort = null) {
        var root = new JsonObject()
            .AddV("scaleset_id", scalesetId)
            .AddV("machine_id", machineId)
            .AddIfNotNullV("dst_port", dstPort);
        return Return<BooleanResult>(await Delete(root));
    }

    public async Task<BooleanResult> Reset(string region) {
        var root = new JsonObject().AddV("region", region);
        var r = await Patch(root);
        return Return<BooleanResult>(r);
    }

    public async Task<Result<ProxyGetResult, Error>> Create(string scalesetId, Guid machineId, int dstPort, int duration) {
        var root = new JsonObject()
            .AddV("scaleset_id", scalesetId)
            .AddV("machine_id", machineId)
            .AddV("dst_port", dstPort)
            .AddV("duration", duration);

        var r = await Post(root);
        return Result<ProxyGetResult>(r);
    }

}
