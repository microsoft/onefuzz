using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;


namespace Tests;

public class EventExportConverterTests {
    enum Color {
        Red,
        Blue
    }

    [Fact]
    public void BaseTypesAreBounded() {
        var a = new {
            guid = Guid.NewGuid(),
            date = new DateTime(),
            en = Color.Red,
            b = 1,
            boo = false,
            flo = float.Pi,
            doub = double.Tau,
            lon = long.MinValue,
            cha = 'a'
        };

        a.GetType().GetProperties().All(p => EventExportConverter.HasBoundedSerialization(p)).Should().BeTrue();
    }

    [Fact]
    public void StringIsNotBounded() {
        var a = new {
            bad = "this is not bounded"
        };

        EventExportConverter.HasBoundedSerialization(a.GetType().GetProperty("bad")!).Should().BeFalse();
    }

    [Fact]
    public void ValidatedStringIsBounded() {
        var a = new {
            scalesetid = ScalesetId.Parse("abc-123")
        };

        EventExportConverter.HasBoundedSerialization(a.GetType().GetProperty("scalesetid")!).Should().BeTrue();
    }

    [Fact]
    public void ComplexObjectsAreSerialized() {
        var randomGuid = Guid.NewGuid();
        var a = new DownloadableEventMessage(
            randomGuid,
            EventType.CrashReported,
            new EventCrashReported(
               new Report(
                   "https://example.com",
                   null,
                   "target.exe",
                   "crash",
                   string.Empty,
                   new List<string> { "this", "is", "a", "stacktrace" },
                   string.Empty,
                   string.Empty,
                   null,
                   Guid.NewGuid(),
                   Guid.NewGuid(),
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
                   null
                   ),
               Container.Parse("this-is-a-container"),
               "crash-abc123",
               null
           ),
           Guid.NewGuid(),
           "onefuzz",
           DateTime.Now,
           new Uri("https://example.com"),
           null
        );
        var serializerOptions = EntityConverter.GetJsonSerializerOptions();
        serializerOptions.Converters.Add(new EventExportConverter());

        var serialized = JsonSerializer.Serialize(a, serializerOptions);

        serialized.Should().NotBeNullOrEmpty();
        serialized.Should().NotContain("stacktrace"); // List<string> is not serialized
        serialized.Should().NotContain("crash-abc123"); // string is not serialized
        serialized.Should().Contain("this-is-a-container"); // ValidatedString is serialized
        serialized.Should().Contain("crash_reported"); // Enum is serialized
        serialized.Should().Contain(DateTime.Now.Year.ToString()); // DateTime is serialized
        serialized.Should().Contain(randomGuid.ToString()); // Guid id serialized
    }

    public class EventExportConverterSerializationTests {
        private readonly JsonSerializerOptions _opts = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions());
        public EventExportConverterSerializationTests() {
            _ = Arb.Register<Arbitraries>();
            _opts.Converters.Add(new EventExportConverter());
        }

        void Test<T>(T v) {
            // TODO: Try cloning/creating a new serializer options from the existing one?
            var serialized = JsonSerializer.Serialize(v, _opts);
            var _ = JsonSerializer.Deserialize<dynamic>(serialized);
        }

        [Property]
        public void EventNodeHeartbeat(EventNodeHeartbeat e) => Test(e);


        [Property]
        public void EventTaskHeartbeat(EventTaskHeartbeat e) => Test(e);

        [Property]
        public void EventTaskStopped(EventTaskStopped e) => Test(e);

        [Property]
        public void EventInstanceConfigUpdated(EventInstanceConfigUpdated e) => Test(e);

        [Property]
        public void EventProxyCreated(EventProxyCreated e) => Test(e);

        [Property]
        public void EventProxyDeleted(EventProxyDeleted e) => Test(e);

        [Property]
        public void EventProxyFailed(EventProxyFailed e) => Test(e);

        [Property]
        public void EventProxyStateUpdated(EventProxyStateUpdated e) => Test(e);


        [Property]
        public void EventCrashReported(EventCrashReported e) => Test(e);


        [Property]
        public void EventRegressionReported(EventRegressionReported e) => Test(e);


        [Property]
        public void EventFileAdded(EventFileAdded e) => Test(e);

        [Property]
        public void EventTaskFailed(EventTaskFailed e) => Test(e);

        [Property]
        public void EventTaskStateUpdated(EventTaskStateUpdated e) => Test(e);

        [Property]
        public void EventScalesetFailed(EventScalesetFailed e) => Test(e);

        [Property]
        public void EventScalesetResizeScheduled(EventScalesetResizeScheduled e) => Test(e);

        [Property]
        public void EventScalesetStateUpdated(EventScalesetStateUpdated e) => Test(e);

        [Property]
        public void EventNodeDeleted(EventNodeDeleted e) => Test(e);

        [Property]
        public void EventNodeCreated(EventNodeCreated e) => Test(e);

        [Property]
        public void EventMessage(DownloadableEventMessage e) => Test(e);
    }

}
