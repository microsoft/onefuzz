using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
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

    // Dry run doesn't make changes
    [Fact]
    public async Async.Task Dry_Run_Does_Not_Make_Changes() {
        await ConfigureAuth();

        var notificationContainer = Container.Parse("abc123");
        var _ = await Context.Containers.CreateContainer(notificationContainer, StorageType.Corpus, null);
        var r = await Context.NotificationOperations.Create(
                notificationContainer,
                new AdoTemplate(
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
                    )),
                    true);

        r.Should().NotBeNull();
        r.IsOk.Should().BeTrue("Failed to create notification for test");

        var notification = r.OkV!;

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new JinjaToScribanMigrationFunction(Logger, auth, Context);
        var req = new JinjaToScribanMigrationPost(DryRun: true);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var dryRunResult = BodyAs<JinjaToScribanMigrationDryRunResponse>(result);
        dryRunResult.NotificationIdsToUpdate.Should().BeEquivalentTo(new List<Guid> { notification.NotificationId });

        Context.NotificationOperations.
    }
    // Migration happens if not dry run
    // Validate ado migration code
    // Validate github migration code


    // Admin is required to execute
    // [Fact]
    public async Async.Task Access_WithoutAuthorization_IsRejected() {

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new JinjaToScribanMigrationFunction(Logger, auth, Context);
        var req = new JinjaToScribanMigrationPost();
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private async Async.Task ConfigureAuth() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } } // needed for admin check
        );

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));
    }
}
