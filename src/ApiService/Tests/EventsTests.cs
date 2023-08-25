using System;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class EventTests {

    [Fact]
    public static void CheckAllEventClass() {
        // instantiate one event to force the static constructor to run
        // if it doesn't throw then this test passes
        _ = new EventPing(Guid.Empty);
    }
}
