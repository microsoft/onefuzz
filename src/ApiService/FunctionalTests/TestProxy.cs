using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests {

    [Trait("Category", "Live")]

    public class TestProxy {

        ProxyApi _proxyApi;
        private readonly ITestOutputHelper _output;

        public TestProxy(ITestOutputHelper output) {
            _output = output;
            _proxyApi = new ProxyApi(ApiClient.Endpoint, ApiClient.Request, output);
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



    }
}
