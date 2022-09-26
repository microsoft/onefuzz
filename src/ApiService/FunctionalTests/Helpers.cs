using Xunit;

namespace FunctionalTests {
    public class Helpers {
        public static async Task<(Pool, Scaleset)> CreatePoolAndScaleset(PoolApi poolApi, ScalesetApi scalesetApi, string os = "linux", string? region = null, int numNodes = 2) {

            var newPoolId = Guid.NewGuid().ToString();
            var newPoolName = PoolApi.TestPoolPrefix + newPoolId;
            var newPool = await poolApi.Create(newPoolName, os);

            Assert.True(newPool.IsOk, $"failed to create new pool: {newPool.ErrorV}");
            var newScalesetResult = await scalesetApi.Create(newPool.OkV!.Name, numNodes, region: region);

            Assert.True(newScalesetResult.IsOk, $"failed to crate new scaleset: {newScalesetResult.ErrorV}");
            var newScaleset = newScalesetResult.OkV!;

            return (newPool.OkV!, newScaleset);

        }
    }
}
