using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;

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
