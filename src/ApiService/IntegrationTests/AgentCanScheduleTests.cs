using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAgentCanScheduleTest : AgentCommandsTestsBase {
    public AzureStorageAgentCanScheduleTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAgentCanScheduleTest : AgentEventsTestsBase {
    public AzuriteAgentCanScheduleTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AgentCanScheduleTestsBase : FunctionTestBase {
    public AgentCanScheduleTestsBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

}
