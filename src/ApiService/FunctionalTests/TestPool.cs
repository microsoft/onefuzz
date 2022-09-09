using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class TestPool {

        PoolApi _poolApi;
        ScalesetApi _scalesetApi;
        private readonly ITestOutputHelper _output;

        public TestPool(ITestOutputHelper output) {
            _output = output;
            _poolApi = new PoolApi(ApiClient.Endpoint, ApiClient.Request, output);
            _scalesetApi = new ScalesetApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        async Task GetNonExistentPool() {
            var p = await _poolApi.Get(name: Guid.NewGuid().ToString());
            Assert.True(p.ErrorV!.UnableToFindPoolError);
        }


        // This for manual test cleanup during development of tests
        //[Fact]
        public async Task DeleteFunctionalTestPools() {
            await _poolApi.DeleteAll();
        }

        [Fact]
        public async Task GetPools() {
            var pools = await _poolApi.Get();
            Assert.True(pools.IsOk);

            if (!pools.OkV!.Any()) {
                _output.WriteLine("Got empty pools");
            } else {
                foreach (var p in pools.OkV!) {
                    _output.WriteLine($"Pool: {p.Name}, PoolId : {p.PoolId}, OS: {p.Os}");
                }
            }
        }


        [Fact]
        public async Task CreateAndDelete() {

            var newPoolId = Guid.NewGuid().ToString();
            var newPoolName = PoolApi.TestPoolPrefix + newPoolId;
            _output.WriteLine($"creating pool {newPoolName}");
            var newPool = await _poolApi.Create(newPoolName, "linux");

            Assert.True(newPool.IsOk, $"failed to create new pool: {newPool.ErrorV}");

            var poolsCreated = await _poolApi.Get();
            Assert.True(poolsCreated.IsOk, $"failed to get pools: {poolsCreated.ErrorV}");

            var newPools = poolsCreated.OkV!.Where(p => p.Name == newPoolName);
            Assert.True(newPools.Count() == 1);

            var deletedPoolResult = await _poolApi.Delete(newPoolName);
            Assert.True(deletedPoolResult.Result);
        }
    }
}
