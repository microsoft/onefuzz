using FluentAssertions;
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

            n.IsOk.Should().BeFalse();
            n.ErrorV!.UnableToFindNode.Should().BeTrue();
        }

        [Fact]
        async Task GetAllNodes() {
            var ns = await _nodeApi.Get();
            ns.IsOk.Should().BeTrue("failed to get all nodes due to {0}", ns.ErrorV);
            foreach (var n in ns.OkV!) {
                _output.WriteLine($"node machine id: {n.MachineId}, scaleset id: {n.ScalesetId}, poolName: {n.PoolName}, poolId: {n.PoolId} state: {n.State}, version: {n.Version}");
            }
        }


        [Fact]
        async Task DeleteNonExistentNode() {
            var n = await _nodeApi.Delete(Guid.NewGuid());
            n.IsError.Should().BeTrue();
            n.Error!.UnableToFindNode.Should().BeTrue();
        }

        [Fact]
        async Task GetPatchPostDelete() {

            var (pool, scaleset) = await Helpers.CreatePoolAndScaleset(_poolApi, _scalesetApi, "linux");

            scaleset = await _scalesetApi.WaitWhile(scaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");
            scaleset.Nodes!.Should().NotBeEmpty();

            var nodeState = scaleset.Nodes!.First();
            var nodeResult = await _nodeApi.Get(nodeState.MachineId);

            nodeResult.IsOk.Should().BeTrue("failed to get node due to {0}", nodeResult.ErrorV);
            var node = nodeResult.OkV!.First();
            node = await _nodeApi.WaitWhile(node.MachineId, n => n.State == "init" || n.State == "setup");

            var r = await _nodeApi.Patch(node.MachineId);
            r.Result.Should().BeTrue();

            var rr = await _nodeApi.Update(node.MachineId, false);

            var d = await _nodeApi.Delete(node.MachineId);
            d.Result.Should().BeTrue();

            var deletePool = await _poolApi.Delete(pool.Name);
            deletePool.Result.Should().BeTrue();
        }
    }
}
