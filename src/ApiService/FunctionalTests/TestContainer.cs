using FluentAssertions;
using Xunit;
using Xunit.Abstractions;


namespace FunctionalTests {
    [Trait("Category", "Live")]
    public class TestContainer {
        private readonly ITestOutputHelper _output;
        private readonly ContainerApi _containerApi;
        private readonly DownloadApi _downloadApi;

        public TestContainer(ITestOutputHelper output) {
            _output = output;
            _containerApi = new ContainerApi(ApiClient.Endpoint, ApiClient.Request, output);
            _downloadApi = new DownloadApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        public async Task DownloadNonExistentContainer() {
            var r1 = await _downloadApi.Get();
            _ = r1.IsOk.Should().BeFalse();
            _ = r1.ErrorV!.Item2!.ShouldBeProvided("container").Should().BeTrue();

            var r2 = await _downloadApi.Get(container: Guid.NewGuid().ToString());
            _ = r2.IsOk.Should().BeFalse();
            _ = r2.ErrorV!.Item2!.ShouldBeProvided("filename").Should().BeTrue();

            var r3 = await _downloadApi.Get(filename: Guid.NewGuid().ToString());
            _ = r3.IsOk.Should().BeFalse();
            _ = r3.ErrorV!.Item2!.ShouldBeProvided("container").Should().BeTrue();

            var r4 = await _downloadApi.Get(container: Guid.NewGuid().ToString(), filename: Guid.NewGuid().ToString());
            _ = r4.IsOk.Should().BeFalse();
            _ = r4.ErrorV!.Item1.Should().Be(System.Net.HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task CreateGetDeleteContainer() {
            var containerName = Guid.NewGuid().ToString();
            var container = await _containerApi.Post(containerName);
            _ = container.IsOk.Should().BeTrue($"failed to create container due to {container.ErrorV}");

            var c = await _containerApi.Get(containerName);


            var d = await _containerApi.Delete(containerName);

            return;

        }


    }
}
