using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

class Pool {

    JsonElement _e;

    public Pool() { }
    public Pool(JsonElement e) => _e = e;

    public string Name => _e.GetProperty("name").GetString()!;
    public string PoolId => _e.GetProperty("pool_id").GetString()!;
    public string Os => _e.GetProperty("os").GetString()!;
}

class PoolApi : ApiBase<Pool> {

    public static string TestPoolPrefix = "FT-DELETE-";

    public PoolApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Pool", request, output) {
    }
    public override Pool Convert(JsonElement e) { return new Pool(e); }

    public async Task<bool> Delete(string name, bool now = true) {
        var root = new JsonObject();
        root.Add("name", JsonValue.Create(name));
        root.Add("now", JsonValue.Create(now));
        return DeleteResult(await Delete(root));
    }

    public async Task<Result<IEnumerable<Pool>, Error>> Get(string? poolName = null, string? poolId = null, string? state = null) {
        var root = new JsonObject();
        root.Add("pool_id", poolId);
        root.Add("name", poolName);
        root.Add("state", state);

        var res = await Get(root);
        return IEnumerableResult(res);
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
                Assert.True(deleted);
            }
        }
    }

    public async Task<Result<Pool, Error>> Create(string poolName, string os, string arch = "x86_64") {
        var rootPoolCreate = new JsonObject();
        rootPoolCreate.Add("name", poolName);
        rootPoolCreate.Add("os", os);
        rootPoolCreate.Add("arch", arch);
        rootPoolCreate.Add("managed", true);
        return Result(await Post(rootPoolCreate));
    }
}
