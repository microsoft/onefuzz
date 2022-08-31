using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

public class Pool : IFromJsonElement<Pool> {

    JsonElement _e;

    public Pool() { }
    public Pool(JsonElement e) => _e = e;

    public string Name => _e.GetProperty("name").GetString()!;
    public string PoolId => _e.GetProperty("pool_id").GetString()!;
    public string Os => _e.GetProperty("os").GetString()!;

    public Pool Convert(JsonElement e) => new Pool(e);
}

public class PoolApi : ApiBase {

    public const string TestPoolPrefix = "FT-DELETE-";

    public PoolApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Pool", request, output) {
    }

    public async Task<BooleanResult> Delete(string name, bool now = true) {
        _output.WriteLine($"deleting pool: {name}, now: {now}");
        var root = new JsonObject();
        root.Add("name", JsonValue.Create(name));
        root.Add("now", JsonValue.Create(now));
        return Return<BooleanResult>(await Delete(root));
    }

    public async Task<Result<IEnumerable<Pool>, Error>> Get(string? name = null, string? id = null, string? state = null) {
        var root = new JsonObject();
        if (id is not null)
            root.Add("pool_id", id);
        if (name is not null)
            root.Add("name", name);
        if (state is not null)
            root.Add("state", state);

        var res = await Get(root);
        return IEnumerableResult<Pool>(res);
    }

    public async Task DeleteAll() {
        var pools = await Get();
        if (!pools.IsOk) {
            throw new Exception($"Failed to get pools due to {pools.ErrorV}");
        }

        foreach (var pool in pools.OkV) {
            if (pool.Name.StartsWith(TestPoolPrefix)) {
                _output.WriteLine($"Deleting {pool.Name}");
                var deleted = await Delete(pool.Name);
                Assert.True(deleted.Result);
            }
        }
    }

    public async Task<Result<Pool, Error>> Create(string poolName, string os, string arch = "x86_64") {
        _output.WriteLine($"creating new pool {poolName} os: {os}");

        var rootPoolCreate = new JsonObject();
        rootPoolCreate.Add("name", poolName);
        rootPoolCreate.Add("os", os);
        rootPoolCreate.Add("arch", arch);
        rootPoolCreate.Add("managed", true);
        return Result<Pool>(await Post(rootPoolCreate));
    }
}
