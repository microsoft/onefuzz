using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class InstanceIdTests {
    [Theory]
    [InlineData("0", 0)]
    [InlineData("ZIK0ZJ", int.MaxValue)]
    [InlineData("1Y2P0IJ32E8E7", long.MaxValue)]
    [InlineData("zik0zj", int.MaxValue)]
    [InlineData("1y2p0ij32e8e7", long.MaxValue)]
    public void CanParseBase36Correctly(string input, long result) {
        Assert.Equal(result, InstanceIds.ReadNumberInBase(input, InstanceIds.Base36));
    }

    [Fact]
    public void CanExtractInstanceIdFromComputerName() {
        // pulled from actual example
        Assert.Equal("1244", InstanceIds.InstanceIdFromComputerName("node0000YK"));
    }

    [Fact]
    public void Base36HasCorrectNumberOfEntries() {
        Assert.Equal(36, InstanceIds.Base36.Length);
    }
}
