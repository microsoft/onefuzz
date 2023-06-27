
using System;
using System.Collections.Generic;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using FluentAssertions;
using Report = Microsoft.OneFuzz.Service.Report;
using Async = System.Threading.Tasks;


namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAdoTest : AdoTestBase {
    public AzureStorageAdoTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAdoTest : AdoTestBase {
    public AzuriteAdoTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AdoTestBase : FunctionTestBase {
    public AdoTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }
    [Fact]
    public async Async.Task TestUnlessClause() {
        var ado = new Ado.AdoConnector(
            new AdoTemplate(
                new Uri("https://example.com"),
                new SecretData<string>(new SecretValue<string>(string.Empty)),
                "some project",
                "some type",
                new(),
                new(),
                new ADODuplicateTemplate(
                    new(),
                    new(),
                    new(),
                    "Some comment",
                    new Dictionary<string, string> {
                        { "System.State", "Closed" },
                        { "System.Reason", "Wont Fix" },
                    }
                )
            ),
            new Ado.Renderer(
                Container.Parse("abc"),
                string.Empty,
                new Report(null, null, string.Empty, string.Empty, string.Empty, new(), string.Empty, string.Empty, null, Guid.Empty, Guid.Empty, null, null, null, null, null, null, null, null, null, null, null, null),
                new Microsoft.OneFuzz.Service.Task(Guid.Empty, Guid.Empty, TaskState.Init, Os.Windows, new TaskConfig(Guid.Empty, null, new TaskDetails(TaskType.LibfuzzerFuzz, 1))),
                new Job(Guid.Empty, JobState.Init, new JobConfig(string.Empty, string.Empty, string.Empty, 1, null)),
                new Uri("https://example.com"),
                new Uri("https://example.com"),
                new Uri("https://example.com"),
                true
            ),
            string.Empty,
            new WorkItemTrackingHttpClient(new Uri("https://example.com"), new VssCredentials()),
            new Uri("https://example.com"),
            LoggerProvider.CreateLogger<Ado>()
        );

        var emptyNotificationInfo = Array.Empty<(string, string)>();

        var workItemMarkedAsWontFix = new WorkItem();
        workItemMarkedAsWontFix.Fields.Add("System.State", "Closed");
        workItemMarkedAsWontFix.Fields.Add("System.Reason", "Wont Fix");

        (await ado.UpdateExisting(workItemMarkedAsWontFix, emptyNotificationInfo)).Should().BeFalse();

        var workItemMarkedClosed = new WorkItem();
        workItemMarkedClosed.Fields.Add("System.State", "Closed");
        workItemMarkedClosed.Fields.Add("System.Reason2", "Completed");

        var action = async () => await ado.UpdateExisting(workItemMarkedClosed, emptyNotificationInfo);

        // Since the `Unless` case won't match, UpdateExisting will attempt to create a comment on the work item
        // The AdoWorkItemClient we configured at the beginning of this test is invalid so the operation will throw
        _ = await action.Should().ThrowAsync<InvalidOperationException>("We passed an invalid client");
    }
}
