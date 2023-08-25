using System;
using FluentAssertions;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;

namespace Tests;

public class ExponentialBackoffTests {
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 25)]
    [InlineData(3, 125)]
    [InlineData(4, 625)]
    public void ExpectedBackoffsWhenLessThanOneDay(int retryAttempt, int expectedBackoffMinutes) {
        var expectedBackoff = TimeSpan.FromMinutes(expectedBackoffMinutes);

        expectedBackoff.Should().Be(QueueFileChanges.CalculateExponentialBackoff(retryAttempt));
    }

    [Fact]
    public void BackoffIsCappedToRoughlyTwoDays() {
        QueueFileChanges.CalculateExponentialBackoff(5).Should()
            .BeLessThan(TimeSpan.FromDays(3));
    }
}
