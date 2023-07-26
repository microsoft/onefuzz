using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Azure.Storage.Blobs;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageEventsTest : EventsTestBase {
    public AzureStorageEventsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteEventsTest : EventsTestBase {
    public AzuriteEventsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class EventsTestBase : FunctionTestBase {
    public EventsTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Fact]
    public async Async.Task BlobIsCreatedAndIsAccessible() {
        var webhookId = Guid.NewGuid();
        var webhookName = "test-webhook";

        var insertWebhook = await Context.WebhookOperations.Insert(
            new Webhook(webhookId, webhookName, null, new List<EventType> { EventType.Ping }, null, WebhookMessageFormat.Onefuzz)
        );
        insertWebhook.IsOk.Should().BeTrue();

        var webhook = await Context.WebhookOperations.GetByWebhookId(webhookId);
        webhook.Should().NotBeNull();

        var ping = await Context.WebhookOperations.Ping(webhook!);
        ping.Should().NotBeNull();

        var msg = TestHttpRequestData.FromJson("GET", new EventsGet(ping.PingId));
        var func = new EventsFunction(Context);
        var result = await func.Run(msg);
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventPayload = BodyAs<EventGetResponse>(result);
        eventPayload.Event.EventType.Should().Be(EventType.Ping);

        var pingEvent = (EventPing)eventPayload.Event.Event;
        pingEvent.PingId.Should().Be(ping.PingId);

        var containerClient = new BlobContainerClient(eventPayload.Event.SasUrl);
        var stream = await containerClient.GetBlobClient(pingEvent.PingId.ToString()).OpenReadAsync();
        using var sr = new StreamReader(stream);
        var eventData = await sr.ReadToEndAsync(); // read to make sure the SAS URL works
        eventData.Should().Contain(ping.PingId.ToString());
    }

    [Fact]
    public async Async.Task UserInfoIsDeserialized() {
        var jobId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskConfig = new TaskConfig(
            jobId,
            null,
            new TaskDetails(
                TaskType.Coverage,
                1
            )
        );
        var webhookId = Guid.NewGuid();
        var webhookName = "test-webhook";
        var appId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var upn = Guid.NewGuid().ToString();

        var insertWebhook = await Context.WebhookOperations.Insert(
            new Webhook(webhookId, webhookName, null, new List<EventType> { EventType.TaskStopped }, null, WebhookMessageFormat.Onefuzz)
        );
        insertWebhook.IsOk.Should().BeTrue();

        await Context.Events.SendEvent(new EventTaskStopped(
            jobId,
            taskId,
            new StoredUserInfo(
                appId,
                objectId),
            taskConfig
        ));

        var webhookMessageLog = await Context.WebhookMessageLogOperations.SearchAll()
            .FirstAsync(wml => wml.WebhookId == webhookId && wml.EventType == EventType.TaskStopped);

        webhookMessageLog.Should().NotBeNull();

        var message = await Context.WebhookOperations.BuildMessage(webhookMessageLog.WebhookId, webhookMessageLog.EventId, webhookMessageLog.EventType, webhookMessageLog.Event, null, WebhookMessageFormat.Onefuzz);

        message.IsOk.Should().BeTrue();
        var eventPayload = message.OkV!.Item1;

        eventPayload.Should()
            .ContainAll(jobId.ToString(), taskId.ToString());
    }
}
