using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class InstanceIdTests {
    [Fact]
    public void CanExtractInstanceIdFromMachineName() {
        Assert.Equal("5", InstanceIds.InstanceIdFromMachineName("node_5"));
    }
}
