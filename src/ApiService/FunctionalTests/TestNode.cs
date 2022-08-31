using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class TestNode {

        NodeApi _nodeApi;
        ScalesetApi _scalesetApi;
        PoolApi _poolApi;
        private readonly ITestOutputHelper _output;

        public TestNode(ITestOutputHelper output) {
            _output = output;
            _nodeApi = new NodeApi(ApiClient.Endpoint, ApiClient.Request, output);
            _scalesetApi = new ScalesetApi(ApiClient.Endpoint, ApiClient.Request, output);
            _poolApi = new PoolApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        async Task GetNonExistentNode() {
            var n = await _nodeApi.Get(Guid.NewGuid());

            Assert.False(n.IsOk);
            Assert.True(n.ErrorV!.UnableToFindNode);
        }

        [Fact]
        async Task GetAllNodes() {
            var ns = await _nodeApi.Get();

            Assert.True(ns.IsOk, $"Failed to get all nodes due to {ns.ErrorV}");
            foreach (var n in ns.OkV!) {
                _output.WriteLine($"node machine id: {n.MachineId}, scaleset id: {n.ScalesetId}, poolName: {n.PoolName}, poolId: {n.PoolId} state: {n.State}, version: {n.Version}");
            }
        }


        [Fact]
        async Task GetPatchPostDelete() {

            var (pool, scaleset) = await Helpers.CreatePoolAndScaleset(_poolApi, _scalesetApi, "linux");

            scaleset = await _scalesetApi.WaitWhile(scaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");
            Assert.True(scaleset.Nodes!.Count > 0);

            var nodeState = scaleset.Nodes!.First();
            var nodeResult = await _nodeApi.Get(nodeState.MachineId);

            Assert.True(nodeResult.IsOk, $"failed to get node due to {nodeResult.ErrorV}");
            var node = nodeResult.OkV!.First();
            node = await _nodeApi.WaitWhile(node.MachineId, n => n.State == "init" || n.State == "setup");

            var r = await _nodeApi.Patch(node.MachineId);
            Assert.True(r.Result);

            var rr = await _nodeApi.Update(node.MachineId, false);

            var d = await _nodeApi.Delete(node.MachineId);
            Assert.True(d.Result);

            var deletePool = await _poolApi.Delete(pool.Name);
            Assert.True(deletePool.Result);
        }

    }
}
