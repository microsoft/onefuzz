using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;
public class Authentication {
    JsonElement _e;

    public Authentication() { }
    public Authentication(JsonElement e) => _e = e;

    string Password => _e.GetProperty("password").GetString()!;

    string PublicKey => _e.GetProperty("public_key").GetString()!;
    string PrivateKey => _e.GetProperty("private_key").GetString()!;
}


public class ScalesetNodeState {
    JsonElement _e;

    public ScalesetNodeState() { }
    public ScalesetNodeState(JsonElement e) => _e = e;

    public Guid MachineId => _e.GetProperty("machine_id").GetGuid();
    public string InstanceId => _e.GetProperty("instance_id").GetString()!;

    public string? NodeState => _e.GetProperty("state").GetString();
}

public class Scaleset : IFromJsonElement<Scaleset> {
    JsonElement _e;
    public Scaleset() { }
    public Scaleset(JsonElement e) => _e = e;

    public Guid ScalesetId => _e.GetProperty("scaleset_id").GetGuid();
    public string PoolName => _e.GetProperty("pool_name").GetString()!;
    public string State => _e.GetProperty("state").GetString()!;

    public Error? Error => _e.GetProperty("error").ValueKind == JsonValueKind.Null ? null : new Error(_e.GetProperty("error"));
    public int Size => _e.GetProperty("size").GetInt32();

    public string VmSku => _e.GetProperty("vm_sku").GetString()!;
    public string Image => _e.GetProperty("image").GetString()!;

    public string Region => _e.GetProperty("region").GetString()!;

    public bool? SpotInstance => _e.GetProperty("spot_instance").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("spot_instance").GetBoolean();

    public bool EphemeralOsDisks => _e.GetProperty("ephemeral_os_disks").GetBoolean();

    public bool NeedsConfigUpdate => _e.GetProperty("needs_config_update").GetBoolean();

    public Dictionary<string, string> Tags => _e.GetProperty("tags").Deserialize<Dictionary<string, string>>()!;

    public Authentication? Auth => _e.GetProperty("auth").ValueKind == JsonValueKind.Null ? null : new Authentication(_e.GetProperty("auth"));

    public Guid? ClientId => _e.GetProperty("client_id").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("client_id").GetGuid();

    public Guid? ClientObjectId => _e.GetProperty("client_object_id").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("client_object_id").GetGuid();

    public List<ScalesetNodeState>? Nodes => _e.GetProperty("nodes").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("nodes").EnumerateArray().Select(node => new ScalesetNodeState(node)).ToList();

    public Scaleset Convert(JsonElement e) => new Scaleset(e);
}

public class ScalesetApi : ApiBase {

    public const string Image_Ubuntu_20_04 = "Canonical:0001-com-ubuntu-server-focal:20_04-lts:latest";

    public ScalesetApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Scaleset", request, output) { }


    public async Task<Result<IEnumerable<Scaleset>, Error>> Get(Guid? id = null, string? state = null, bool? includeAuth = false) {
        var root = new JsonObject();
        if (id is not null)
            root.Add("scaleset_id", id);
        if (state is not null)
            root.Add("state", state);
        if (includeAuth is not null)
            root.Add("include_auth", includeAuth);
        var res = await Get(root);
        return IEnumerableResult<Scaleset>(res);
    }

    public async Task<Result<Scaleset, Error>> Create(string poolName, int size, string? region=null, string vmSku = "Standard_D2s_v3", string image = Image_Ubuntu_20_04, bool spotInstance = false) {
        var rootScalesetCreate = new JsonObject();
        rootScalesetCreate.Add("pool_name", poolName);
        rootScalesetCreate.Add("vm_sku", vmSku);
        rootScalesetCreate.Add("image", image);
        rootScalesetCreate.Add("size", size);
        rootScalesetCreate.Add("spot_instances", spotInstance);
        if (region is not null)
            rootScalesetCreate.Add("region", region);

        var tags = new JsonObject();
        tags.Add("Purpose", "Functional-Test");
        rootScalesetCreate.Add("tags", tags);

        return Result<Scaleset>(await Post(rootScalesetCreate));
    }

    public async Task<Result<Scaleset, Error>> Patch(Guid id, int size) {
        var scalesetPatch = new JsonObject();
        scalesetPatch.Add("scaleset_id", id);
        scalesetPatch.Add("size", size);
        return Result<Scaleset>(await Patch(scalesetPatch));
    }

    public async Task<BooleanResult> Delete(Guid id, bool now) {
        var scalesetDelete = new JsonObject();
        scalesetDelete.Add("scaleset_id", id);
        scalesetDelete.Add("now", now);

        return Return<BooleanResult>(await Delete(scalesetDelete));
    }


    public async Task<Scaleset> WaitWhile(Guid id, Func<Scaleset, bool> wait) {
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
