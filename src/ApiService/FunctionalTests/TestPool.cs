using FluentAssertions;
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
        public async Task GetNonExistentPool() {
            var p = await _poolApi.Get(name: Guid.NewGuid().ToString());
            _ = p.ErrorV!.UnableToFindPoolError.Should().BeTrue("{0}", p.ErrorV!);
        }


        // This for manual test cleanup during development of tests
        [Fact(Skip = "not actually a test")]
        public async Task DeleteFunctionalTestPools() {
            await _poolApi.DeleteAll();
        }

        [Fact]
        public async Task GetPools() {
            var pools = await _poolApi.Get();
            _ = pools.IsOk.Should().BeTrue();

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

            _ = newPool.IsOk.Should().BeTrue("failed to create new pool: {0}", newPool.ErrorV);

            var poolsCreated = await _poolApi.Get();
            _ = poolsCreated.IsOk.Should().BeTrue("failed to get pools: {0}", poolsCreated.ErrorV);

            var newPools = poolsCreated.OkV!.Where(p => p.Name == newPoolName);
            _ = newPools.Count().Should().Be(1);

            var deletedPoolResult = await _poolApi.Delete(newPoolName);
            _ = deletedPoolResult.Result.Should().BeTrue();
        }
    }
}
