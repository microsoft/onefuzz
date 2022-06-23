using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class ValidatedStringTests {

    record ThingContainingPoolName(PoolName PoolName);

    [Fact]
    public void PoolNameValidatesOnDeserialization() {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ThingContainingPoolName>("{ \"PoolName\": \"is-not!-a-pool\" }"));
        Assert.Equal("unable to parse input as a PoolName", ex.Message);
    }

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
}
