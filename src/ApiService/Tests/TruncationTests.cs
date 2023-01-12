using System;
using System.Collections.Generic;
using Microsoft.OneFuzz.Service;
using Xunit;
using FluentAssertions;

namespace Tests;

public class TruncationTests {
    [Fact]
    public static void ReportIsTruncatable() {
        var report = GenerateReport();

        var truncatedReport = report.Truncate(3);

        truncatedReport.Executable.Should().Be("SOM");
        truncatedReport.CallStack.Count.Should().Be(0);
    }

    [Fact]
    public static void TestListTruncation() {
        var testList = new List<string> {
            "1", "2", "3", "456"
        };

        var truncatedList = TruncateUtils.TruncateList(testList, 3);
        truncatedList.Count.Should().Be(3);
        truncatedList.Should().BeEquivalentTo(new[] { "1", "2", "3" });
    }

    [Fact]
    public static void TestNestedTruncation() {
        var eventCrashReported = new EventCrashReported(
            GenerateReport(),
            Container.Parse("123"),
            "abc",
            null
        );

        var truncatedEvent = eventCrashReported.Truncate(3) as EventCrashReported;
        truncatedEvent.Should().NotBeNull();
        truncatedEvent?.Report.Executable.Should().Be("SOM");
        truncatedEvent?.Report.CallStack.Count.Should().Be(0);
    }

    private static Report GenerateReport() {
        return new Report(
            null,
            null,
            "SOMESUPRTLONGSTRINGSOMESUPRTLONGSTRINGSOMESUPRTLONGSTRINGSOMESUPRTLONGSTRING",
            "abc",
            "abc",
            new List<string> { "SOMESUPRTLONGSTRINGSOMESUPRTLONGSTRING" },
            "abc",
            "abc",
            null,
            Guid.Empty,
            Guid.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Uri("http://example.com")
        );
    }
}
