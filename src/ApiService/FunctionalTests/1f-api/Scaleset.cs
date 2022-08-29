using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;

class Scaleset {
    JsonElement _e;
    public Scaleset() { }
    public Scaleset(JsonElement e) => _e = e;

    public Guid ScalesetId => _e.GetProperty("scaleset_id").GetGuid();
    public string PoolName => _e.GetProperty("pool_name").GetString()!;
    public string State => _e.GetProperty("state").GetString()!;

    public string Error => _e.GetProperty("error").GetRawText();
    public int Size => _e.GetProperty("size").GetInt32();
}

class ScalesetApi : ApiBase<Scaleset> {

    public const string Image_Ubuntu_20_04 = "Canonical:0001-com-ubuntu-server-focal:20_04-lts:latest";

    public ScalesetApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Scaleset", request, output) { }


    public override Scaleset Convert(JsonElement e) { return new Scaleset(e); }

    public async Task<Result<IEnumerable<Scaleset>, Error>> Get(Guid? id = null, string? state = null, bool? includeAuth = false) {
        var root = new JsonObject();
        root.Add("scaleset_id", id);
        root.Add("state", state);
        root.Add("include_auth", includeAuth);
        var res = await Get(root);
        return IEnumerableResult(res);
    }

    public async Task<Result<Scaleset, Error>> Create(string poolName, int size, string vmSku = "Standard_D2s_v3", string image = Image_Ubuntu_20_04, bool spotInstance = false) {
        var rootScalesetCreate = new JsonObject();
        rootScalesetCreate.Add("pool_name", poolName);
        rootScalesetCreate.Add("vm_sku", vmSku);
        rootScalesetCreate.Add("image", image);
        rootScalesetCreate.Add("size", size);
        rootScalesetCreate.Add("spot_instance", spotInstance);

        var tags = new JsonObject();
        tags.Add("Purpose", "Functional-Test");
        rootScalesetCreate.Add("tags", tags);

        return Result(await Post(rootScalesetCreate));
    }

    public async Task<Result<Scaleset, Error>> Patch(Guid id, int size) {
        var scalesetPatch = new JsonObject();
        scalesetPatch.Add("scaleset_id", id);
        scalesetPatch.Add("size", size);
        return Result(await Patch(scalesetPatch));
    }

    public async Task<bool> Delete(Guid id, bool now) {
        var scalesetDelete = new JsonObject();
        scalesetDelete.Add("scaleset_id", id);
        scalesetDelete.Add("now", now);

        return DeleteResult(await Delete(scalesetDelete));
    }

}
