using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

public class Pool : IFromJsonElement<Pool> {

    readonly JsonElement _e;

    public Pool(JsonElement e) => _e = e;
    public static Pool Convert(JsonElement e) => new(e);

    public string Name => _e.GetStringProperty("name");
    public string PoolId => _e.GetStringProperty("pool_id");
    public string Os => _e.GetStringProperty("os");
}

public class PoolApi : ApiBase {

    public const string TestPoolPrefix = "FT-DELETE-";

    public PoolApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Pool", request, output) {
    }

    public async Task<BooleanResult> Delete(string name, bool now = true) {
        _output.WriteLine($"deleting pool: {name}, now: {now}");
        var root = new JsonObject()
            .AddV("name", name)
            .AddV("now", now);
        return Return<BooleanResult>(await Delete(root));
    }

    public async Task<Result<IEnumerable<Pool>, Error>> Get(string? name = null, string? id = null, string? state = null) {
        var root = new JsonObject()
            .AddIfNotNullV("pool_id", id)
            .AddIfNotNullV("name", name)
            .AddIfNotNullV("state", state);
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
                var deleted = await Delete(pool.Name);
                Assert.True(deleted.Result);
            }
        }
    }

    public async Task<Result<Pool, Error>> Create(string poolName, string os, string arch = "x86_64", bool managed = true) {
        _output.WriteLine($"creating new pool {poolName} os: {os}");

        var rootPoolCreate = new JsonObject()
            .AddV("name", poolName)
            .AddV("os", os)
            .AddV("arch", arch)
            .AddV("managed", managed);
        return Result<Pool>(await Post(rootPoolCreate));
    }
}
