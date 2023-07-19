using FluentAssertions;
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
            scalesets.IsOk.Should().BeTrue("failed to get scalesets due to {0}", scalesets.ErrorV);
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

        private async Task CreateAndDelete(string os) {
            var (newPool, newScaleset) = await Helpers.CreatePoolAndScaleset(_poolApi, _scalesetApi, os);

            var newScalesetResultAgain = await _scalesetApi.Create(newPool.Name, 2);
            var newScalesetResultAgainAgain = await _scalesetApi.Create(newPool.Name, 5);

            try {
                _output.WriteLine($"New scale set info id: {newScaleset.ScalesetId}, pool: {newScaleset.PoolName}, state: {newScaleset.State}, error: {newScaleset.Error}");

                var scalesetsCreated = await _scalesetApi.Get();
                scalesetsCreated.IsOk.Should().BeTrue("failed to get scalesets: {0}", scalesetsCreated.ErrorV);

                var poolsCreated = await _poolApi.Get();
                poolsCreated.IsOk.Should().BeTrue("failed to get pools: {0}", poolsCreated.ErrorV);

                var newPools = poolsCreated.OkV!.Where(p => p.Name == newPool.Name);
                var newScalesets = scalesetsCreated.OkV!.Where(sc => sc.ScalesetId == newScaleset.ScalesetId);

                newPools.Count().Should().Be(1);
                newScalesets.Count().Should().Be(1);

                Console.WriteLine($"Waiting for scaleset to move out from Init State");
                newScaleset = await _scalesetApi.WaitWhile(newScaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");

                _output.WriteLine($"Scaleset is in {newScaleset.State}");

                if (newScaleset.State == "creation_failed") {
                    throw new Exception($"Scaleset creation failed due {newScaleset.Error}");
                } else if (newScaleset.State != "running") {
                    throw new Exception($"Expected scaleset to be in Running state, instead got {newScaleset.State}");
                }

                var patch0 = await _scalesetApi.Patch(newScaleset.ScalesetId, 0);
                patch0.IsOk.Should().BeFalse();
                patch0.ErrorV!.IsWrongSizeError.Should().BeTrue();
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
                var deletedPoolResult = await _poolApi.Delete(newPool.Name);

                preDeleteScalesets.IsOk.Should().BeTrue("failed to get pre-deleted scalesets due to: {0}", preDeleteScalesets.ErrorV);
                var preDelete = preDeleteScalesets.OkV!.Where(sc => sc.PoolName == newPool.Name);
                preDelete.Count().Should().Be(3);

                Result<IEnumerable<Pool>, Error> deletedPool;
                do {
                    await Task.Delay(TimeSpan.FromSeconds(10.0));
                    deletedPool = await _poolApi.Get(newPool.Name);

                } while (deletedPool.IsOk);

                deletedPool.ErrorV!.UnableToFindPoolError.Should().BeTrue();
                var postDeleteScalesets = await _scalesetApi.Get();
                postDeleteScalesets.IsOk.Should().BeTrue("failed to get scalesets after finishing pool deletion due to {0}", postDeleteScalesets.ErrorV);

                _output.WriteLine($"Pool is deleted {newPool.Name}");

                var postDelete = postDeleteScalesets.OkV!.Where(sc => sc.PoolName == newPool.Name);
                postDelete.Should().BeEmpty();
                var patch1 = await _scalesetApi.Patch(newScaleset.ScalesetId, 1);
                patch1.IsOk.Should().BeFalse();
                patch1.ErrorV!.UnableToFindScalesetError.Should().BeTrue();
            }
            return;

        }


        [Fact]
        public async Task CreateAndDeleteLinux() {
            await CreateAndDelete("linux");
        }

        [Fact]
        public async Task CreateAndDeleteWindows() {
            await CreateAndDelete("windows");
        }
    }
}
