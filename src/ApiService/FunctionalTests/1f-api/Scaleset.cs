using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

public class ScalesetNodeState : IFromJsonElement<ScalesetNodeState> {
    readonly JsonElement _e;

    public ScalesetNodeState(JsonElement e) => _e = e;

    public static ScalesetNodeState Convert(JsonElement e) => new(e);

    public Guid MachineId => _e.GetGuidProperty("machine_id");
    public string InstanceId => _e.GetStringProperty("instance_id");

    public string? NodeState => _e.GetNullableStringProperty("state");
}

public class Scaleset : IFromJsonElement<Scaleset> {
    readonly JsonElement _e;
    public Scaleset(JsonElement e) => _e = e;
    public static Scaleset Convert(JsonElement e) => new(e);

    public string ScalesetId => _e.GetStringProperty("scaleset_id");
    public string PoolName => _e.GetStringProperty("pool_name");
    public string State => _e.GetStringProperty("state");

    public Error? Error => _e.GetNullableObjectProperty<Error>("error");

    public long Size => _e.GetLongProperty("size");

    public string VmSku => _e.GetStringProperty("vm_sku");
    public string Image => _e.GetStringProperty("image");

    public string Region => _e.GetStringProperty("region");

    public bool? SpotInstance => _e.GetNullableBoolProperty("spot_instance");

    public bool EphemeralOsDisks => _e.GetBoolProperty("ephemeral_os_disks");

    public bool NeedsConfigUpdate => _e.GetBoolProperty("needs_config_update");

    public IDictionary<string, string> Tags => _e.GetStringDictProperty("tags");

    public Authentication? Auth => _e.GetNullableObjectProperty<Authentication>("auth");

    public Guid? ClientId => _e.GetNullableGuidProperty("client_id");

    public Guid? ClientObjectId => _e.GetNullableGuidProperty("client_object_id");

    public IEnumerable<ScalesetNodeState>? Nodes => _e.GetEnumerableNullableProperty<ScalesetNodeState>("nodes");
}

public class ScalesetApi : ApiBase {

    public ScalesetApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Scaleset", request, output) { }


    public async Task<Result<IEnumerable<Scaleset>, Error>> Get(string? id = null, string? state = null, bool? includeAuth = false) {
        var j = new JsonObject()
            .AddIfNotNullV("scaleset_id", id)
            .AddIfNotNullV("state", state)
            .AddIfNotNullV("include_auth", includeAuth);
        var res = await Get(j);
        return IEnumerableResult<Scaleset>(res);
    }

    public async Task<Result<Scaleset, Error>> Create(string poolName, int size, string? region = null, string vmSku = "Standard_D2s_v3", string? image = null, bool spotInstance = false) {
        _output.WriteLine($"Creating scaleset in pool {poolName}, size: {size}");

        var rootScalesetCreate = new JsonObject()
            .AddV("pool_name", poolName)
            .AddV("vm_sku", vmSku)
            .AddV("image", image)
            .AddV("size", size)
            .AddV("spot_instances", spotInstance)
            .AddIfNotNullV("region", region);

        rootScalesetCreate.Add("tags", new JsonObject().AddV("Purpose", "Functional-Test"));

        return Result<Scaleset>(await Post(rootScalesetCreate));
    }

    public async Task<Result<Scaleset, Error>> Patch(string id, int size) {
        var scalesetPatch = new JsonObject()
            .AddV("scaleset_id", id)
            .AddV("size", size);
        return Result<Scaleset>(await Patch(scalesetPatch));
    }

    public async Task<BooleanResult> Delete(string id, bool now) {
        var scalesetDelete = new JsonObject()
            .AddV("scaleset_id", id)
            .AddV("now", now);
        return Return<BooleanResult>(await Delete(scalesetDelete));
    }


    public async Task<Scaleset> WaitWhile(string id, Func<Scaleset, bool> wait) {
        var currentState = "";
        Scaleset newScaleset;
        do {
            await Task.Delay(TimeSpan.FromSeconds(10.0));
            var sc = await Get(id: id);
            Assert.True(sc.IsOk, $"failed to get scaleset with id: {id} due to {sc.ErrorV}");
            newScaleset = sc.OkV!.First();

            if (currentState != newScaleset.State) {
                _output.WriteLine($"Scaleset is in {newScaleset.State}");
                currentState = newScaleset.State;
            }
        } while (wait(newScaleset));

        return newScaleset;
    }

}
