using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class JsonTests {
    private static IContainerDef? Roundtrip(IContainerDef def)
        => JsonSerializer.Deserialize<IContainerDef>(JsonSerializer.Serialize(def));

    [Fact]
    public void CanRoundtripMultipleContainer() {
        var it = new MultipleContainer(new List<SyncedDir>{
            new SyncedDir("path", new Uri("https://example.com/1")),
            new SyncedDir("path2", new Uri("https://example.com/2")),
        });

        var result = Roundtrip(it);
        var multiple = Assert.IsType<MultipleContainer>(result);
        Assert.Equal(it.SyncedDirs, multiple.SyncedDirs);
    }

    [Fact]
    public void CanRoundtripSingleContainer() {
        var it = new SingleContainer(new SyncedDir("path", new Uri("https://example.com")));

        var result = Roundtrip(it);
        var single = Assert.IsType<SingleContainer>(result);
        Assert.Equal(it.SyncedDir, single.SyncedDir);
    }
}
