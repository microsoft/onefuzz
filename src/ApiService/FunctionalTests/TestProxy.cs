﻿using FluentAssertions;
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

            allProxiesResult.IsOk.Should().BeTrue("failed to get proxies due to {0}", allProxiesResult.ErrorV);

            if (!allProxiesResult.OkV!.Any()) {
                _output.WriteLine("Got empty list of proxies");
            } else {
                foreach (var p in allProxiesResult.OkV!) {
                    _output.WriteLine($"ProxyId: {p.ProxyId} vm state: {p.VmState}, region: {p.Region}");
                }
            }
        }

        //TODO: do not run this for now - this triggers: https://github.com/microsoft/onefuzz/issues/2331
        //[Fact]
        public async Task CreateResetDelete() {
            var (newPool, newScaleset) = await Helpers.CreatePoolAndScaleset(_poolApi, _scalesetApi, "linux");

            newScaleset = await _scalesetApi.WaitWhile(newScaleset.ScalesetId, sc => sc.State == "init" || sc.State == "setup");
            newScaleset.Nodes!.Should().NotBeEmpty();

            var firstNode = newScaleset.Nodes!.First();

            var nodeResult = await _nodeApi.Get(machineId: firstNode.MachineId);
            nodeResult.IsOk.Should().BeTrue();
            var node = nodeResult.OkV!.First();

            node = await _nodeApi.WaitWhile(node.MachineId, n => n.State == "init" || n.State == "setup");

            var proxy = await _proxyApi.Create(newScaleset.ScalesetId, node.MachineId, 2223, 1);

            var proxyAgain = await _proxyApi.Create(newScaleset.ScalesetId, node.MachineId, 2223, 1);

            proxy.IsOk.Should().BeTrue("failed to create proxy due to {0}", proxy.ErrorV);
            proxyAgain.IsOk.Should().BeTrue("failed to create proxy with same config due to {0}", proxyAgain.ErrorV);

            proxy.OkV!.Should().BeEquivalentTo(proxyAgain.OkV!);
            _output.WriteLine($"created proxy dst ip: {proxy.OkV!.Forward.DstIp}, srcPort: {proxy.OkV.Forward.SrcPort} dstport: {proxy.OkV!.Forward.DstPort}, ip: {proxy.OkV!.Ip}");


            var proxyReset = await _proxyApi.Reset(newScaleset.Region);
            proxyReset.Result.Should().BeTrue();

            var deleteProxy = await _proxyApi.Delete(newScaleset.ScalesetId, node.MachineId);
            deleteProxy.Result.Should().BeTrue();

            _output.WriteLine($"deleted proxy");

            var deletePool = await _poolApi.Delete(newPool.Name);
            deletePool.Result.Should().BeTrue();
            _output.WriteLine($"deleted pool {newPool.Name}");
        }
    }
}
