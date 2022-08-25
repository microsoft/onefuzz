using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OneFuzz.Service;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class Scalesets {
        static Uri endpoint = new Uri("http://localhost:7071");

        static Microsoft.Morse.AuthenticationConfig authConfig =
                new Microsoft.Morse.AuthenticationConfig(
                    ClientId: System.Environment.GetEnvironmentVariable("ONEFUZZ_CLIENT_ID")!,
                    TenantId: System.Environment.GetEnvironmentVariable("ONEFUZZ_TENANT_ID")!,
                    Scopes: new[] { System.Environment.GetEnvironmentVariable("ONEFUZZ_SCOPES")! },
                    Secret: System.Environment.GetEnvironmentVariable("ONEFUZZ_SECRET")!);

        static Microsoft.Morse.ServiceAuth auth = new Microsoft.Morse.ServiceAuth(authConfig);
        static Microsoft.OneFuzz.Service.Request request = new Microsoft.OneFuzz.Service.Request(new HttpClient(), () => auth.Token(new CancellationToken()));

        JsonSerializerOptions _opts = Microsoft.OneFuzz.Service.OneFuzzLib.Orm.EntityConverter.GetJsonSerializerOptions();

        Uri poolEndpoint = new Uri(endpoint, "/api/Pool");
        Uri scalesetEndpoint = new Uri(endpoint, "/api/Scaleset");


        string serialize<T>(T x) {
            return JsonSerializer.Serialize(x, _opts);
        }

        T? deserialize<T>(string json) {
            return JsonSerializer.Deserialize<T>(json, _opts);
        }

        private readonly ITestOutputHelper output;
        public Scalesets(ITestOutputHelper output) {
            this.output = output;
        }

        public async Task<HttpResponseMessage> DeletePool(PoolName name) {
            var root = new JsonObject();
            root.Add("name", JsonValue.Create(name));
            root.Add("now", JsonValue.Create(true));
            var body = root.ToJsonString();
            var r = await request.Delete(poolEndpoint, body); ;
            return r;
        }

        async Task<Pool> GetPool(string poolName) {
            var root = new JsonObject();
            root.Add("pool_id", null);
            root.Add("name", poolName);
            root.Add("state", null);

            var body = root.ToJsonString();
            var r2 = await request.Get(poolEndpoint, body);
            var resPoolSearch = await r2.Content.ReadAsStringAsync();
            var doc = await System.Text.Json.JsonDocument.ParseAsync(r2.Content.ReadAsStream());
            return deserialize<Pool>(resPoolSearch)!;
        }


        async Task<Pool[]> GetAllPools() {
            var root = new JsonObject();
            root.Add("pool_id", null);
            root.Add("name", null);
            root.Add("state", null);

            var body = root.ToJsonString();
            var r2 = await request.Get(poolEndpoint, body);
            var resPoolSearch = await r2.Content.ReadAsStringAsync();
            var doc = await System.Text.Json.JsonDocument.ParseAsync(r2.Content.ReadAsStream());
            return deserialize<Pool[]>(resPoolSearch)!;
        }

        async Task<Scaleset[]> GetAllScalesets() {
            var root = new JsonObject();
            root.Add("scaleset_id", null);
            root.Add("state", null);
            root.Add("include_auth", false);

            var scalesetSearchBody = root.ToJsonString();
            var r = await request.Get(scalesetEndpoint, scalesetSearchBody);
            var scalesets = deserialize<Scaleset[]>(await r.Content.ReadAsStringAsync());
            return scalesets!;
        }

        async Task<Scaleset?> GetScaleset(Guid id) {
            var root = new JsonObject();
            root.Add("scaleset_id", id.ToString());
            root.Add("state", null);
            root.Add("include_auth", false);

            var scalesetSearchBody = root.ToJsonString();
            var r = await request.Get(scalesetEndpoint, scalesetSearchBody);
            var s = await r.Content.ReadAsStringAsync();
            var scaleset = deserialize<Scaleset>(s);
            return scaleset;
        }

        async System.Threading.Tasks.Task DeleteAllTestPools() {
            var pools = await GetAllPools();
            foreach (var p in pools) {
                if (p.Name.String.StartsWith("FT-DELETE-")) {
                    output.WriteLine($"Deleting {p.Name}");
                    await DeletePool(p.Name);
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task DeleteFunctionalTestPools() {
            await DeleteAllTestPools();
        }

        [Fact]
        public async System.Threading.Tasks.Task GetPoolsAndScalesets() {
            var scalesets = await GetAllScalesets();
            if (scalesets is null) {
                output.WriteLine("Got null when getting scalesets");
            } else if (scalesets.Length == 0) {
                output.WriteLine("Got empty scalesets");
            } else {
                foreach (var sc in scalesets!) {
                    output.WriteLine($"Pool: {sc.PoolName} Scaleset: {sc.ScalesetId}");
                }
            }

            var pools = await GetAllPools();

            if (pools is null) {
                output.WriteLine("Got null when getting pools");
            } else if (pools.Length == 0) {
                output.WriteLine("Got empty pools");
            } else {
                foreach (var p in pools) {
                    output.WriteLine($"Pool: {p.Name}, PoolId : {p.PoolId}, OS: {p.Os}");
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task CreateAndDelete() {
            var scalesets = await GetAllScalesets();
            var pools = await GetAllPools();

            var newPoolId = System.Guid.NewGuid().ToString();
            var newPoolName = "FT-DELETE-" + newPoolId;

            try {
                Pool? newPool;
                {
                    var rootPoolCreate = new JsonObject();
                    rootPoolCreate.Add("name", newPoolName);
                    rootPoolCreate.Add("os", "linux");
                    rootPoolCreate.Add("architecture", "x86_64");
                    rootPoolCreate.Add("managed", true);

                    var newPoolCreate = rootPoolCreate.ToJsonString();

                    var r = await request.Post(poolEndpoint, newPoolCreate);
                    var s = await r.Content.ReadAsStringAsync();
                    newPool = deserialize<Pool>(s);
                }

                Scaleset? newScaleset;
                {
                    var rootScalesetCreate = new JsonObject();
                    rootScalesetCreate.Add("pool_name", newPool!.Name.String);
                    rootScalesetCreate.Add("vm_sku", "Standard_D2s_v3");
                    rootScalesetCreate.Add("image", "Canonical:0001-com-ubuntu-server-focal:20_04-lts:latest");
                    rootScalesetCreate.Add("size", 2);
                    rootScalesetCreate.Add("spot_instance", false);
                    var tags = new JsonObject();
                    tags.Add("Purpose", "Functional-Test");
                    rootScalesetCreate.Add("tags", tags);

                    var newScalesetCreate = rootScalesetCreate.ToJsonString();

                    var r = await request.Post(scalesetEndpoint, newScalesetCreate);
                    var s = await r.Content.ReadAsStringAsync();
                    newScaleset = deserialize<Scaleset>(s);
                }

                output.WriteLine($"New scale set info id: {newScaleset!.ScalesetId}, pool: {newScaleset!.PoolName}, state: {newScaleset.State}, error: {newScaleset.Error}");

                var scalesetsCreated = await GetAllScalesets();
                var poolsCreated = await GetAllPools();

                var newPools = poolsCreated.Where(p => p.Name.String == newPoolName);
                var newScalesets = scalesetsCreated.Where(sc => sc.ScalesetId == newScaleset.ScalesetId);

                Assert.True(newPools.Count() == 1);
                Assert.True(newScalesets.Count() == 1);


                var currentState = ScalesetState.Init;
                System.Console.WriteLine($"Waiting for scaleset to move out from Init State");
                while (newScaleset.State == ScalesetState.Init || newScaleset.State == ScalesetState.Setup) {
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10.0));
                    newScaleset = await GetScaleset(id: newScaleset.ScalesetId);
                    if (currentState != newScaleset!.State) {
                        output.WriteLine($"Scaleset is in {newScaleset.State}");
                        currentState = newScaleset!.State;
                    }
                }
                output.WriteLine($"Scaleset is in {newScaleset.State}");

                if (currentState == ScalesetState.CreationFailed) {
                    throw new Exception($"Scaleset creation failed due {newScaleset.Error}");
                } else if (currentState != ScalesetState.Running) {
                    throw new Exception($"Expected scaleset to be in Running state, instead got {currentState}");
                }
            } finally {
                var preDelete = (await GetAllScalesets()).Where(sc => sc.PoolName.String == newPoolName);
                Assert.True(preDelete.Count() == 1);

                await DeletePool(new PoolName(newPoolName));

                Pool deletedPool;
                do {
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10.0));
                    deletedPool = await GetPool(newPoolName);
                } while (deletedPool != null);
                var postDelete = (await GetAllScalesets()).Where(sc => sc.PoolName.String == newPoolName);
                Assert.True(postDelete.Any() == false);
            }
            return;
        }
    }
}
