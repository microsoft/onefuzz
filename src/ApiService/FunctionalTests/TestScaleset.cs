using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {
    [Trait("Category", "Live")]

    public class TestScaleset {

        ScalesetApi _scalesetApi;
        PoolApi _poolApi;
        private readonly ITestOutputHelper _output;

        public TestScaleset(ITestOutputHelper output) {
            this._output = output;
            _scalesetApi = new ScalesetApi(ApiClient.Endpoint, ApiClient.Request, output);
            _poolApi = new PoolApi(ApiClient.Endpoint, ApiClient.Request, output);
        }


        [Fact]
        public async Task GetScalesets() {
            var scalesets = await _scalesetApi.Get();
            Assert.True(scalesets.IsOk, $"failed to get scalesets due to {scalesets.ErrorV}");
            if (!scalesets.OkV!.Any()) {
                _output.WriteLine("Got empty scalesets");
            } else {
                
                foreach (var sc in scalesets.OkV!) {
                    if (sc.Error is not null)
                        _output.WriteLine($"Pool: {sc.PoolName} Scaleset: {sc.ScalesetId}, state; {sc.State}, error: {sc.Error}");
                    else
                        _output.WriteLine($"Pool: {sc.PoolName} Scaleset: {sc.ScalesetId}, state; {sc.State}");
                }
            }
        }

        [Fact]
        public async Task CreateAndDelete() {
            var newPoolId = Guid.NewGuid().ToString();
            var newPoolName = PoolApi.TestPoolPrefix + newPoolId;
            var newPool = await _poolApi.Create(newPoolName, "linux");

            Assert.True(newPool.IsOk, $"failed to create new pool: {newPool.ErrorV}");

            var newScalesetResult = await _scalesetApi.Create(newPool.OkV!.Name, 2);

            Assert.True(newScalesetResult.IsOk, $"failed to crate new scaleset: {newScalesetResult.ErrorV}");
            var newScaleset = newScalesetResult.OkV!;

            try {
                _output.WriteLine($"New scale set info id: {newScaleset.ScalesetId}, pool: {newScaleset.PoolName}, state: {newScaleset.State}, error: {newScaleset.Error}");

                var scalesetsCreated = await _scalesetApi.Get();
                Assert.True(scalesetsCreated.IsOk, $"failed to get scalesets: {scalesetsCreated.ErrorV}");

                var poolsCreated = await _poolApi.Get();
                Assert.True(poolsCreated.IsOk, $"failed to get pools: {poolsCreated.ErrorV}");

                var newPools = poolsCreated.OkV!.Where(p => p.Name == newPoolName);
                var newScalesets = scalesetsCreated.OkV!.Where(sc => sc.ScalesetId == newScaleset.ScalesetId);

                Assert.True(newPools.Count() == 1);
                Assert.True(newScalesets.Count() == 1);

                Console.WriteLine($"Waiting for scaleset to move out from Init State");
                newScaleset = await _scalesetApi.WaitWhile(newScaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");

                _output.WriteLine($"Scaleset is in {newScaleset.State}");

                if (newScaleset.State == "creation_failed") {
                    throw new Exception($"Scaleset creation failed due {newScaleset.Error}");
                } else if (newScaleset.State != "running") {
                    throw new Exception($"Expected scaleset to be in Running state, instead got {newScaleset.State}");
                }

                var patch0 = await _scalesetApi.Patch(newScaleset.ScalesetId, 0);
                Assert.False(patch0.IsOk);
                Assert.True(patch0.ErrorV!.IsWrongSizeError);
                // https://github.com/microsoft/onefuzz/issues/2311
                //var patch1 = await _scalesetApi.Patch(newScaleset.ScalesetId, 1);
                //Assert.True(patch1.IsOk, $"scaleset patch failed due to: {patch1}");
                //Assert.True(patch1.OkV!.Size == 1);

                //var patch3 = await _scalesetApi.Patch(newScaleset.ScalesetId, 3);
                //Assert.True(patch3.IsOk);
                //Assert.True(patch3.OkV!.Size == 3);

                newScaleset = await _scalesetApi.WaitWhile(newScaleset.ScalesetId, sc => sc.State == "resize");

                if (newScaleset.State == "creation_failed") {
                    throw new Exception($"Scaleset creation failed due {newScaleset.Error}");
                } else if (newScaleset.State != "running") {
                    throw new Exception($"Expected scaleset to be in Running state, instead got {newScaleset.State}");
                }
            } finally {
                var preDeleteScalesets = await _scalesetApi.Get();
                var deletedPoolResult = await _poolApi.Delete(newPoolName);

                Assert.True(preDeleteScalesets.IsOk, $"failed to get pre-deleted scalesets due to: {preDeleteScalesets.ErrorV}");
                var preDelete = preDeleteScalesets.OkV!.Where(sc => sc.PoolName == newPoolName);
                Assert.True(preDelete.Count() == 1);

                Result<IEnumerable<Pool>, Error> deletedPool;
                do {
                    await Task.Delay(TimeSpan.FromSeconds(10.0));
                    deletedPool = await _poolApi.Get(newPoolName);

                } while (deletedPool.IsOk);
                Assert.True(deletedPool.ErrorV!.UnableToFindPoolError);
                var postDeleteScalesets = await _scalesetApi.Get();
                Assert.True(postDeleteScalesets.IsOk, $"failed to get scalesets after finishing pool deletion due to {postDeleteScalesets.ErrorV}");

                _output.WriteLine($"Pool is deleted {newPoolName}");

                var postDelete = postDeleteScalesets.OkV!.Where(sc => sc.PoolName == newPoolName);
                Assert.False(postDelete.Any());
                var patch1 = await _scalesetApi.Patch(newScaleset.ScalesetId, 1);
                Assert.False(patch1.IsOk);
                Assert.True(patch1.ErrorV!.UnableToFindScalesetError);
            }
            return;
        }
    }
}
