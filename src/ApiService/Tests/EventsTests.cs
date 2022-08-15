using System;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class EventTests {

    [Fact]
    static void CheckAllEventClass() {
        // instantiate one event to force the static constructor to run
        var testEvent = new EventPing(Guid.Empty);

    }
}
