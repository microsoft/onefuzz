using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class TestProxy {

        ProxyApi _proxyApi;
        ScalesetApi _scalesetApi;
        PoolApi _poolApi;
        NodeApi _nodeApi;
        private readonly ITestOutputHelper _output;

        public TestProxy(ITestOutputHelper output) {
            _output = output;
            _proxyApi = new ProxyApi(ApiClient.Endpoint, ApiClient.Request, output);
            _scalesetApi = new ScalesetApi(ApiClient.Endpoint, ApiClient.Request, output);
            _poolApi = new PoolApi(ApiClient.Endpoint, ApiClient.Request, output);
            _nodeApi = new NodeApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        public async Task GetProxies() {
            var allProxiesResult = await _proxyApi.Get();
            Assert.True(allProxiesResult.IsOk, $"failed to get proxies due to {allProxiesResult.ErrorV}");

            if (!allProxiesResult.OkV!.Any()) {
                _output.WriteLine("Got empty list of proxies");
            } else {
                foreach (var p in allProxiesResult.OkV!) {
                    _output.WriteLine($"ProxyId: {p.ProxyId} vm state: {p.VmState}, region: {p.Region}");
                }
            }
        }

        [Fact]
        public async Task CreateResetDelete() {
            var newPoolId = Guid.NewGuid().ToString();
            var newPoolName = PoolApi.TestPoolPrefix + newPoolId;
            var newPool = await _poolApi.Create(newPoolName, "linux");

            Assert.True(newPool.IsOk, $"failed to create new pool: {newPool.ErrorV}");

            var newScalesetResult = await _scalesetApi.Create(newPool.OkV!.Name, 2);

            Assert.True(newScalesetResult.IsOk, $"failed to crate new scaleset: {newScalesetResult.ErrorV}");
            var newScaleset = newScalesetResult.OkV!;

            newScaleset = await _scalesetApi.WaitWhile(newScaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");

            Assert.True(newScaleset.Nodes!.Count > 0);

            var firstNode = newScaleset.Nodes.First();

            var nodeResult = await _nodeApi.Get(machineId: firstNode.MachineId);
            Assert.True(nodeResult.IsOk);
            var node = nodeResult.OkV!.First();

            node = await _nodeApi.WaitWhile(node.MachineId, n => n.State == "init" || n.State == "setup");

            var proxy = await _proxyApi.Create(newScaleset.ScalesetId, node.MachineId, 2222, 1);

            var proxyAgain = await _proxyApi.Create(newScaleset.ScalesetId, node.MachineId, 2222, 1);

            Assert.True(proxy.IsOk, $"failed to create proxy due to {proxy.ErrorV}");

            _output.WriteLine($"created proxy dst ip: {proxy.OkV!.Forward.DstIp}, srcPort: {proxy.OkV.Forward.SrcPort} dstport: {proxy.OkV!.Forward.DstPort}, ip: {proxy.OkV!.Ip}");

            //TODO: compare proxies, result should be the same
            Assert.True(proxyAgain.IsOk, $"failed to create proxy with same config due to {proxyAgain.ErrorV}");


            var proxyReset = await _proxyApi.Reset(newScaleset.Region);

            var deleteProxy = await _proxyApi.Delete(node.MachineId);
            Assert.True(deleteProxy.Result);

            _output.WriteLine($"deleted proxy");

            var deletePool = await _poolApi.Delete(newPoolName);
            Assert.True(deletePool.Result);
        }


    }
}
