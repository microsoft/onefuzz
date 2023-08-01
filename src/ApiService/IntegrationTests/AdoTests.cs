
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.OneFuzz.Service;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.OneFuzz.Service.NotificationsBase;
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
            new RenderedAdoTemplate(
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
                    new List<Dictionary<string, string>> {
                        new () {
                            { "System.State", "Closed" },
                            { "System.Reason", "Wont Fix" },
                        },
                        new () {
                            { "System.State", "Closed" },
                            { "System.Reason", "No Repro" },
                        }
                    }
                )
            ),
            string.Empty,
            new WorkItemTrackingHttpClient(new Uri("https://example.com"), new VssCredentials()),
            new Uri("https://example.com"),
            LoggerProvider.CreateLogger<Ado>(),
            new()
        );

        var emptyNotificationInfo = Array.Empty<(string, string)>();

        var workItemMarkedAsWontFix = new WorkItem();
        workItemMarkedAsWontFix.Fields.Add("System.State", "Closed");
        workItemMarkedAsWontFix.Fields.Add("System.Reason", "Wont Fix");

        (await ado.UpdateExisting(workItemMarkedAsWontFix, emptyNotificationInfo)).Should().BeFalse();

        var workItemMarkedAsNoRepro = new WorkItem();
        workItemMarkedAsNoRepro.Fields.Add("System.State", "Closed");
        workItemMarkedAsNoRepro.Fields.Add("System.Reason", "No Repro");

        (await ado.UpdateExisting(workItemMarkedAsNoRepro, emptyNotificationInfo)).Should().BeFalse();

        var workItemMarkedClosed = new WorkItem();
        workItemMarkedClosed.Fields.Add("System.State", "Closed");
        workItemMarkedClosed.Fields.Add("System.Reason2", "Completed");

        var action = async () => await ado.UpdateExisting(workItemMarkedClosed, emptyNotificationInfo);

        // Since the `Unless` case won't match, UpdateExisting will attempt to create a comment on the work item
        // The AdoWorkItemClient we configured at the beginning of this test is invalid so the operation will throw
        _ = await action.Should().ThrowAsync<InvalidOperationException>("We passed an invalid client");
    }

    [Fact]
    public void TestTruncatedTitleQuerying() {
        var ado = new Ado.AdoConnector(
            new RenderedAdoTemplate(
                new Uri("https://example.com"),
                new SecretData<string>(new SecretValue<string>(string.Empty)),
                "some project",
                "some type",
                new List<string> { "System.Title" },
                new Dictionary<string, string>() {
                    {"System.Title", "At this point the title would've already been truncated"},
                },
                new ADODuplicateTemplate(
                    new(),
                    new(),
                    new(),
                    "Some comment"
                )
            ),
            string.Empty,
            new WorkItemTrackingHttpClient(new Uri("https://example.com"), new VssCredentials()),
            new Uri("https://example.com"),
            LoggerProvider.CreateLogger<Ado>(),
            new Dictionary<string, WorkItemField2> {
                {
                    "system.title",
                    new WorkItemField2 {
                        ReferenceName = "System.Title",
                        SupportedOperations = new List<WorkItemFieldOperation> {
                            new WorkItemFieldOperation { ReferenceName = "SupportedOperations.Equals" }
                        }
                    }
                },
            }
        );

        var (wiql, postQueryFilter) = ado.CreateExistingWorkItemsQuery(new List<(string, string)>());

        postQueryFilter.Should().BeEmpty("All unique_fields are validFields");
        wiql.Query.Should().Contain("system.title");
    }

    // TODO: Test render template function that it actually truncates
    [Fact]
    public async Async.Task TitleTruncatesWhenRenderingAdoTemplate() {
        var logTracer = LoggerProvider.CreateLogger<Ado>();
        var instanceUrl = new Uri("https://example.com");
        var report = Tests.TruncationTests.GenerateReport();
        var issueTitle = string.Join(" ", Enumerable.Repeat("{{ report.executable }}", 100));
        var filename = "abc.txt";
        var container = Container.Parse("abc");
        var project = "project";
        var type = "bug";
        var renderer = await Renderer.ConstructRenderer(Context, container, filename, issueTitle, report, instanceUrl, logTracer, GenerateTask(), GenerateJob(), instanceUrl, instanceUrl, instanceUrl);
        var adoTemplate = new AdoTemplate(
            instanceUrl,
            new SecretData<string>(new SecretValue<string>("secret")),
            project,
            type,
            new List<string> { "System.Title" },
            new Dictionary<string, string> { { "System.Title", issueTitle } },
            new ADODuplicateTemplate(
                new(),
                new(),
                new Dictionary<string, string> { { "System.Title", issueTitle } }
            )
        );

        var renderedTemplate = Ado.RenderAdoTemplate(logTracer, renderer, adoTemplate, instanceUrl);

        issueTitle.Length.Should().BeGreaterThan(128, "The title needs to be long enough to require truncation");
        renderedTemplate.AdoFields["System.Title"].Length.Should().Be(128);
        renderedTemplate.AdoFields["System.Title"].Should().Be(renderedTemplate.OnDuplicate.AdoFields["System.Title"]);
    }

    private static Task GenerateTask() {
        return new Task(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TaskState.Running,
            Os.Windows,
            new TaskConfig(
                Guid.NewGuid(),
                null,
                new TaskDetails(
                    TaskType.LibfuzzerFuzz,
                    1
                )
            )
        );
    }

    private static Job GenerateJob() {
        return new Job(
            Guid.NewGuid(),
            JobState.Enabled,
            new JobConfig(
                "job-project",
                "job-name",
                "job-build",
                1,
                null
            ),
            null
        );
    }
}
