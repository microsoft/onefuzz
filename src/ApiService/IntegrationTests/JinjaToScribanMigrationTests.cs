using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;
using JinjaToScribanMigrationFunction = Microsoft.OneFuzz.Service.Functions.JinjaToScriban;

namespace IntegrationTests;

public class AzuriteJinjaToScribanMigrationTest : JinjaToScribanMigrationTestBase {
    public AzuriteJinjaToScribanMigrationTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class JinjaToScribanMigrationTestBase : FunctionTestBase {
    private readonly Guid _userObjectId = Guid.NewGuid();

    protected JinjaToScribanMigrationTestBase(ITestOutputHelper output, IStorage storage) : base(output, storage) {
    }

    [Fact]
    public async Async.Task Dry_Run_Does_Not_Make_Changes() {
        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                MigratableAdoTemplate(),
                true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notificationBefore = r.OkV!;
        var adoTemplateBefore = (notificationBefore.Config as AdoTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost(DryRun: true);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var dryRunResult = BodyAs<JinjaToScribanMigrationDryRunResponse>(result);
        dryRunResult.NotificationIdsToUpdate.Should().BeEquivalentTo(new List<Guid> { notificationBefore.NotificationId });

        var notificationAfter = (await Context.NotificationOperations.GetNotification(notificationBefore.NotificationId))!;
        var adoTemplateAfter = (notificationAfter.Config as AdoTemplate)!;

        notificationBefore.Should().BeEquivalentTo(notificationAfter, options =>
            options
                .Excluding(o => o.Timestamp)
                .Excluding(o => o.ETag));

        adoTemplateBefore.Should().BeEquivalentTo(adoTemplateAfter);
    }

    [Fact]
    public async Async.Task Migration_Happens_When_Not_Dry_run() {
        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                MigratableAdoTemplate(),
                true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notificationBefore = r.OkV!;
        var adoTemplateBefore = (notificationBefore.Config as AdoTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var migrationResult = BodyAs<JinjaToScribanMigrationResponse>(result);
        migrationResult.FailedNotificationIds.Should().BeEmpty();
        migrationResult.UpdatedNotificationIds.Should().BeEquivalentTo(new List<Guid> { notificationBefore.NotificationId });

        var notificationAfter = (await Context.NotificationOperations.GetNotification(notificationBefore.NotificationId))!;
        var adoTemplateAfter = (notificationAfter.Config as AdoTemplate)!;

        adoTemplateBefore.Should().NotBeEquivalentTo(adoTemplateAfter);

        var template = (notificationAfter.Config as AdoTemplate)!;
        template.Project.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(MigratableAdoTemplate().Project));
    }

    [Fact]
    public async Async.Task OptionalFieldsAreSupported() {
        var adoTemplate = new AdoTemplate(
            new Uri("http://example.com"),
            new SecretData<string>(new SecretValue<string>("some secret")),
            "{{ report.input_blob.container }}",
            "{{ if org }} blah {{ end }}",
            Array.Empty<string>().ToList(),
            new Dictionary<string, string> {
                        { "abc", "{{ if org }} blah {{ end }}"}
            },
            new ADODuplicateTemplate(
                Array.Empty<string>().ToList(),
                new Dictionary<string, string>(),
                new Dictionary<string, string> {
                            { "onDuplicateComment", "{{ if org }} blah {{ end }}" }
                },
                "{{ if org }} blah {{ end }}"
            ),
            "{{ if org }} blah {{ end }}"
        );

        (await JinjaTemplateAdapter.IsValidScribanNotificationTemplate(Context, Logger, adoTemplate)).Should().BeTrue();
    }

    [Fact]
    public async Async.Task All_ADO_Fields_Are_Migrated() {
        var notificationContainer = Container.Parse("abc123");
        var adoTemplate = new AdoTemplate(
            new Uri("http://example.com"),
            new SecretData<string>(new SecretValue<string>("some secret")),
            "{% if org %} blah {% endif %}",
            "{% if org %} type {% endif %}",
            Array.Empty<string>().ToList(),
            new Dictionary<string, string> {
                { "abc", "{% if org %} comment {% endif %}"}
            },
            new ADODuplicateTemplate(
                Array.Empty<string>().ToList(),
                new Dictionary<string, string>(),
                new Dictionary<string, string> {
                    { "onDuplicateComment", "{% if org %} comment {% endif %}" }
                },
                "{% if org %} comment {% endif %}"
            ),
            "{% if org %} comment {% endif %}"
        );

        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                adoTemplate,
                true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notificationBefore = r.OkV!;
        var adoTemplateBefore = (notificationBefore.Config as AdoTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var migrationResult = BodyAs<JinjaToScribanMigrationResponse>(result);
        migrationResult.FailedNotificationIds.Should().BeEmpty();
        migrationResult.UpdatedNotificationIds.Should().BeEquivalentTo(new List<Guid> { notificationBefore.NotificationId });

        var notificationAfter = (await Context.NotificationOperations.GetNotification(notificationBefore.NotificationId))!;
        var adoTemplateAfter = (notificationAfter.Config as AdoTemplate)!;

        adoTemplateBefore.Should().NotBeEquivalentTo(adoTemplateAfter);

        adoTemplateAfter.Project.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.Project));
        adoTemplateAfter.Comment.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.Comment!));
        adoTemplateAfter.Type.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.Type));

        foreach (var item in adoTemplateAfter.AdoFields) {
            adoTemplateAfter.AdoFields[item.Key]
                .Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.AdoFields[item.Key]));
        }

        foreach (var item in adoTemplateAfter.OnDuplicate.AdoFields) {
            adoTemplateAfter.OnDuplicate.AdoFields[item.Key]
                .Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.OnDuplicate.AdoFields[item.Key]));
        }

        adoTemplateAfter.OnDuplicate.Comment.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.OnDuplicate.Comment!));
    }

    [Fact]
    public async Async.Task All_Github_Fields_Are_Migrated() {
        var githubTemplate = MigratableGithubTemplate();

        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                githubTemplate,
                true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notificationBefore = r.OkV!;
        var githubTemplateBefore = (notificationBefore.Config as GithubIssuesTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var migrationResult = BodyAs<JinjaToScribanMigrationResponse>(result);
        migrationResult.FailedNotificationIds.Should().BeEmpty();
        migrationResult.UpdatedNotificationIds.Should().BeEquivalentTo(new List<Guid> { notificationBefore.NotificationId });

        var notificationAfter = (await Context.NotificationOperations.GetNotification(notificationBefore.NotificationId))!;
        var githubTemplateAfter = (notificationAfter.Config as GithubIssuesTemplate)!;

        githubTemplateBefore.Should().NotBeEquivalentTo(githubTemplateAfter);

        githubTemplateAfter.UniqueSearch.str.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.UniqueSearch.str));
        githubTemplateAfter.UniqueSearch.Author.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.UniqueSearch.Author!));

        githubTemplateAfter.Title.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.Title));
        githubTemplateAfter.Body.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.Body));

        githubTemplateAfter.OnDuplicate.Comment.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.OnDuplicate.Comment!));
        githubTemplateAfter.OnDuplicate.Labels.Should().BeEquivalentTo(
            githubTemplateBefore.OnDuplicate.Labels.Select(l => JinjaTemplateAdapter.AdaptForScriban(l)).ToList()
        );

        githubTemplateAfter.Assignees.Should().BeEquivalentTo(
            githubTemplateBefore.Assignees.Select(a => JinjaTemplateAdapter.AdaptForScriban(a)).ToList()
        );
        githubTemplateAfter.Labels.Should().BeEquivalentTo(
            githubTemplateBefore.Labels.Select(l => JinjaTemplateAdapter.AdaptForScriban(l)).ToList()
        );
        githubTemplateAfter.Organization.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.Organization));
        githubTemplateAfter.Repository.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.Repository));
    }

    [Fact]
    public async Async.Task Teams_Template_Not_Migrated() {
        var teamsTemplate = GetTeamsTemplate();
        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                teamsTemplate,
                true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notificationBefore = r.OkV!;
        var teamsTemplateBefore = (notificationBefore.Config as TeamsTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var migrationResult = BodyAs<JinjaToScribanMigrationResponse>(result);
        migrationResult.FailedNotificationIds.Should().BeEmpty();
        migrationResult.UpdatedNotificationIds.Should().BeEmpty();

        var notificationAfter = (await Context.NotificationOperations.GetNotification(notificationBefore.NotificationId))!;
        var teamsTemplateAfter = (notificationAfter.Config as TeamsTemplate)!;

        teamsTemplateBefore.Should().BeEquivalentTo(teamsTemplateAfter);
    }

    // Multiple notification configs can be migrated
    [Fact]
    public async Async.Task Can_Migrate_Multiple_Notification_Configs() {
        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateNewContainer(notificationContainer, StorageType.Corpus, null);

        var teamsTemplate = GetTeamsTemplate();
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                teamsTemplate,
                false);
        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");
        var teamsNotificationBefore = r.OkV!;
        var teamsTemplateBefore = (teamsNotificationBefore.Config as TeamsTemplate)!;

        var adoTemplate = MigratableAdoTemplate();
        r = await Context.NotificationOperations.Create(
            notificationContainer,
            adoTemplate,
            false);
        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue();
        var adoNotificationBefore = r.OkV!;
        var adoTemplateBefore = (adoNotificationBefore.Config as AdoTemplate)!;

        var githubTemplate = MigratableGithubTemplate();
        r = await Context.NotificationOperations.Create(
            notificationContainer,
            githubTemplate,
            false);
        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue();
        var githubNotificationBefore = r.OkV!;
        var githubTemplateBefore = (githubNotificationBefore.Config as GithubIssuesTemplate)!;

        var func = new JinjaToScribanMigrationFunction(LoggerProvider.CreateLogger<JinjaToScriban>(), Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var teamsNotificationAfter = (await Context.NotificationOperations.GetNotification(teamsNotificationBefore.NotificationId))!;
        var adoNotificationAfter = (await Context.NotificationOperations.GetNotification(adoNotificationBefore.NotificationId))!;
        var githubNotificationAfter = (await Context.NotificationOperations.GetNotification(githubNotificationBefore.NotificationId))!;

        var migrationResult = BodyAs<JinjaToScribanMigrationResponse>(result);
        migrationResult.FailedNotificationIds.Should().BeEmpty();
        migrationResult.UpdatedNotificationIds.Should()
            .ContainEquivalentOf(adoNotificationBefore.NotificationId).And
            .ContainEquivalentOf(githubNotificationBefore.NotificationId).And
            .NotContainEquivalentOf(teamsNotificationBefore.NotificationId);

        var teamsTemplateAfter = (teamsNotificationAfter.Config as TeamsTemplate)!;
        teamsTemplateAfter.Should().BeEquivalentTo(teamsTemplateBefore);

        var adoTemplateAfter = (adoNotificationAfter.Config as AdoTemplate)!;
        adoTemplateAfter.Project.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(adoTemplateBefore.Project));

        var githubTemplateAfter = (githubNotificationAfter.Config as GithubIssuesTemplate)!;
        githubTemplateAfter.Organization.Should().BeEquivalentTo(JinjaTemplateAdapter.AdaptForScriban(githubTemplateBefore.Organization));
    }

    [Fact]
    public async Async.Task Do_Not_Enforce_Key_Exists_In_Strict_Validation() {
        (await JinjaTemplateAdapter.IsValidScribanNotificationTemplate(Context, Logger, ValidScribanAdoTemplate()))
            .Should().BeTrue();
    }

    private static AdoTemplate MigratableAdoTemplate() {
        return new AdoTemplate(
            new Uri("http://example.com"),
            new SecretData<string>(new SecretValue<string>("some secret")),
            "{% if org %} blah {% endif %}",
            string.Empty,
            Array.Empty<string>().ToList(),
            new Dictionary<string, string>(),
            new ADODuplicateTemplate(
                Array.Empty<string>().ToList(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>()
        ));
    }

    private static GithubIssuesTemplate MigratableGithubTemplate() {
        return new GithubIssuesTemplate(
            new SecretData<GithubAuth>(new SecretValue<GithubAuth>(new GithubAuth("abc", "123"))),
            "{% if org %} blah {% endif %}",
            "{% if org %} blah {% endif %}",
            "{% if org %} blah {% endif %}",
            "{% if org %} blah {% endif %}",
            new GithubIssueSearch(
                new List<GithubIssueSearchMatch>(),
                "{% if org %} blah {% endif %}",
                "{% if org %} blah {% endif %}"
            ),
            new List<string> { "{% if org %} blah {% endif %}" },
            new List<string> { "{% if org %} blah {% endif %}" },
            new GithubIssueDuplicate(
                new List<string> { "{% if org %} blah {% endif %}" },
                true,
                "{% if org %} blah {% endif %}"
            )
        );
    }

    private static TeamsTemplate GetTeamsTemplate() {
        return new TeamsTemplate(new SecretData<string>(new SecretValue<string>("https://example.com")));
    }

    private static AdoTemplate ValidScribanAdoTemplate() {
        return new AdoTemplate(
            new Uri("http://example.com"),
            new SecretData<string>(new SecretValue<string>("some secret")),
            "{{ if task.tags.project }} blah {{ end }}",
            string.Empty,
            Array.Empty<string>().ToList(),
            new Dictionary<string, string>(),
            new ADODuplicateTemplate(
                Array.Empty<string>().ToList(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>()
        ));
    }
}
