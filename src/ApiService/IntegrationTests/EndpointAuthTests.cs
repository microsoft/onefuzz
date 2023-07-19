
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;


[Trait("Category", "Live")]
public class AzureStorageEndpointAuthTest : EndpointAuthTestBase {
    public AzureStorageEndpointAuthTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteEndpointAuthTest : EndpointAuthTestBase {
    public AzuriteEndpointAuthTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class EndpointAuthTestBase : FunctionTestBase {
    public EndpointAuthTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) {
    }

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _userObjectId = Guid.NewGuid();

    private Task<OneFuzzResultVoid> CheckUserAdmin() {
        var userAuthInfo = new UserAuthInfo(
            new UserInfo(ApplicationId: _applicationId, ObjectId: _userObjectId, "upn"),
            new List<string>());

        var auth = new EndpointAuthorization(Context, LoggerProvider.CreateLogger<EndpointAuthorization>(), null!);

        return auth.CheckRequireAdmins(userAuthInfo);
    }

    [Fact]
    public async Async.Task IfRequireAdminPrivilegesIsEnabled_UserIsNotPermitted() {
        // config specifies that a different user is admin
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                RequireAdminPrivileges = true,
            });

        var result = await CheckUserAdmin();
        Assert.False(result.IsOk, "should not be admin");
    }

    [Fact]
    public async Async.Task IfRequireAdminPrivilegesIsDisabled_UserIsPermitted() {
        // disable requiring admin privileges
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                RequireAdminPrivileges = false,
            });

        var result = await CheckUserAdmin();
        Assert.True(result.IsOk, "should be admin");
    }

    [Fact]
    public async Async.Task EnablingAdminForAnotherUserDoesNotPermitThisUser() {
        var otherUserObjectId = Guid.NewGuid();

        // config specifies that a different user is admin
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                Admins = new[] { otherUserObjectId },
                RequireAdminPrivileges = true,
            });

        var result = await CheckUserAdmin();
        Assert.False(result.IsOk, "should not be admin");
    }

    [Fact]
    public async Async.Task UserCanBeAdmin() {
        // config specifies that user is admin
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                Admins = new[] { _userObjectId },
                RequireAdminPrivileges = true,
            });

        var result = await CheckUserAdmin();
        Assert.True(result.IsOk, "should be admin");
    }
}
