using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class ValidatedStringTests {

    record ThingContainingPoolName(PoolName PoolName);

    [Fact]
    public void PoolNameDeserializesFromString() {
        var result = JsonSerializer.Deserialize<ThingContainingPoolName>("{  \"PoolName\": \"is-a-pool\" }");
        Assert.Equal("is-a-pool", result?.PoolName.String);
    }

    [Fact]
    public void PoolNameSerializesToString() {
        var result = JsonSerializer.Serialize(new ThingContainingPoolName(PoolName.Parse("is-a-pool")));
        Assert.Equal("{\"PoolName\":\"is-a-pool\"}", result);
    }

    [Theory]
    [InlineData("x", false)] // too short
    [InlineData("xy", false)] // too short
    [InlineData("xyz", true)]
    [InlineData("-container", false)] // can't start with hyphen
    [InlineData("container-", true)] // can end with hyphen
    [InlineData("container-name", true)] // can have middle hyphen
    [InlineData("container--name", false)] // can't have two consecutive hyphens
    [InlineData("container-Name", false)] // can't have capitals
    [InlineData("container-name-09", true)] // can have numbers
    public void ContainerNames(string name, bool valid) {
        Assert.Equal(valid, Container.TryParse(name, out var _));
    }
}
