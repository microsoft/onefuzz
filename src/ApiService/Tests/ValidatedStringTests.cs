using System;
using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class ValidatedStringTests {

    sealed record ThingContainingPoolName(PoolName PoolName);

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
        Assert.Equal(valid, Container.IsValid(name));
    }

    [Theory(Skip = "Validation is disabled for now")]
    [InlineData("xyz", true)]
    [InlineData("", false)]
    [InlineData("Default-Ubuntu20.04-Standard_D2", true)]
    [InlineData("Default!", false)]
    public void PoolNames(string name, bool valid) {
        Assert.Equal(valid, PoolName.IsValid(name));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("abc", true)]
    [InlineData("a-bc", true)]
    [InlineData("-abc", false)]
    [InlineData("abc-", false)]
    [InlineData("ef052a0d-f235-4115-bd47-b359bcc5078b", true)]
    public void ScalesetIds(string name, bool valid) {
        Assert.Equal(valid, ScalesetId.IsValid(name));
    }

    private static readonly Guid _fixedGuid = Guid.Parse("3b24ba21-1cad-4b07-8655-914754485838");

    [Fact]
    public void ScalesetId_FromBasicPool() {
        var pool = PoolName.Parse("pool");
        var id = Scaleset.GenerateNewScalesetIdUsingGuid(pool, _fixedGuid).ToString();
        Assert.Equal("pool-3b24ba211cad4b078655914754485838", id);
    }

    [Fact]
    public void ScalesetId_FromReallyLongPool() {
        var pool = PoolName.Parse(new string('x', 100));
        var id = Scaleset.GenerateNewScalesetIdUsingGuid(pool, _fixedGuid).ToString();
        Assert.Equal(64, id.Length);
        Assert.Equal($"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx-3b24ba211cad4b078655914754485838", id);
    }

    [Fact]
    public void ScalesetId_FromPoolWithBadCharacters() {
        var pool = PoolName.Parse("_.-po-!?(*!&@#$)o_.l-._");
        var id = Scaleset.GenerateNewScalesetIdUsingGuid(pool, _fixedGuid).ToString();
        // hyphens preserved except at start and end, and underscores turned into hyphens
        Assert.Equal($"po-o-l-3b24ba211cad4b078655914754485838", id);
    }
}
