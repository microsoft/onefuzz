using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;

public class ReproConfig : IFromJsonElement<ReproConfig> {
    readonly JsonElement _e;

    public ReproConfig(JsonElement e) => _e = e;

    public static ReproConfig Convert(JsonElement e) => new(e);

    public string Container => _e.GetStringProperty("container");
    public string Path => _e.GetStringProperty("path");

    public long Duration => _e.GetLongProperty("duration");
}

public class Repro : IFromJsonElement<Repro> {

    readonly JsonElement _e;

    public Repro(JsonElement e) => _e = e;

    public static Repro Convert(JsonElement e) => new(e);

    public Guid VmId => _e.GetGuidProperty("vm_id");

    public Guid TaskId => _e.GetGuidProperty("task_id");

    public string Os => _e.GetStringProperty("os");

    public string? Ip => _e.GetNullableStringProperty("ip");

    public DateTimeOffset? EndTime => _e.GetNullableDateTimeOffsetProperty("end_time");

    public string State => _e.GetStringProperty("state");

    public Error? Error => _e.GetNullableObjectProperty<Error>("error");

    public Authentication? Auth => _e.GetNullableObjectProperty<Authentication>("auth");

    ReproConfig Config => _e.GetObjectProperty<ReproConfig>("config");

    UserInfo? UserInfo => _e.GetNullableObjectProperty<UserInfo>("user_info");
}


public class ReproVmss : ApiBase {


    public ReproVmss(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/repro_vms", request, output) {
    }

    public async Task<Result<Repro, Error>> Get(Guid? vmId) {
        var n = new JsonObject().AddV("vm_id", vmId);

        var r = await Get(n);
        return Result<Repro>(r);
    }

    public async Task<Result<Repro, Error>> Post(string container, string path, long duration) {
        var n = new JsonObject()
            .AddV("container", container)
            .AddV("path", path)
            .AddV("duration", duration);

        var r = await Post(n);
        return Result<Repro>(r);
    }

    public async Task<Result<Repro, Error>> Delete(Guid? vmId) {
        var n = new JsonObject()
            .AddV("vm_id", vmId);
        var r = await Delete(n);
        return Result<Repro>(r);
    }

}
