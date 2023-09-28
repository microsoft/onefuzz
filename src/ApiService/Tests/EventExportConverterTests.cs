using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;


namespace Tests;

public class EventExportConverterTests
{
    [Fact]
    public void BaseTypesAreBounded()
    {
        var a = 1;
        Events.EventExportConverter.HasBoundedSerialization(a.GetType().GetProperties().First());
    }

    [Fact]
    public void StringIsNotBounded() { }

    [Fact]
    public void ValidatedStringIsBounded() { }

    [Fact]
    public void ComplexObjectsAreSerialized() { }
}
