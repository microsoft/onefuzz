using FluentAssertions;
using Xunit;
using Xunit.Abstractions;


namespace FunctionalTests {
    [Trait("Category", "Live")]
    public class TestInfo {
        private readonly ITestOutputHelper _output;
        InfoApi _infoApi;
        public TestInfo(ITestOutputHelper output) {
            _output = output;
            _infoApi = new InfoApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        async Task GetInfo() {
            var info = await _infoApi.Get();
            info.IsOk.Should().BeTrue();
            info.OkV!.Versions.ContainsKey("onefuzz").Should().BeTrue();
        }
    }
}
