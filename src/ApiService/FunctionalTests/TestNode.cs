using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class TestNode {

        NodeApi _nodeApi;
        private readonly ITestOutputHelper _output;

        public TestNode(ITestOutputHelper output) {
            _output = output;
            _nodeApi = new NodeApi(ApiClient.Endpoint, ApiClient.Request, output);
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


    }
}
