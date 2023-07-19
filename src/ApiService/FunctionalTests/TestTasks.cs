using FluentAssertions;
using Xunit;
using Xunit.Abstractions;


namespace FunctionalTests {
    [Trait("Category", "Live")]
    public class TestTasks {
        TaskApi _taskApi;

        private readonly ITestOutputHelper _output;

        public TestTasks(ITestOutputHelper output) {
            this._output = output;
            _taskApi = new TaskApi(ApiClient.Endpoint, ApiClient.Request, output);
        }

        [Fact]
        public async Task GetNonExistentTask() {
            var t1 = await _taskApi.Get(Guid.NewGuid());
            t1.IsOk.Should().BeTrue();
            t1.OkV.Should().BeEmpty();


            var t2 = await _taskApi.Get(Guid.NewGuid(), Guid.NewGuid());
            t2.IsOk.Should().BeFalse();
            t2.ErrorV!.UnableToFindTask.Should().BeTrue();
        }


    }
}
