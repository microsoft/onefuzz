﻿using System;
using System.Net;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAgentCommandsTest : AgentCommandsTestsBase {
    public AzureStorageAgentCommandsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAgentCommandsTest : AgentEventsTestsBase {
    public AzuriteAgentCommandsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AgentCommandsTestsBase : FunctionTestBase {
    public AgentCommandsTestsBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }


    [Fact]
    public async Async.Task Authorization_IsRequired() {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCommands(LoggerProvider.CreateLogger<AgentCommands>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task UserAuthorization_IsNotPermitted() {
        var auth = new TestEndpointAuthorization(RequestType.User, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCommands(LoggerProvider.CreateLogger<AgentCommands>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task AgentAuthorization_IsAccepted() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCommands(LoggerProvider.CreateLogger<AgentCommands>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); // BadRequest due to no body, not Unauthorized
    }

    [Fact]
    public async Async.Task AgentCommand_GetsCommand() {
        var machineId = Guid.NewGuid();
        var messageId = Guid.NewGuid().ToString();
        var command = new NodeCommand {
            Stop = new StopNodeCommand()
        };
        await Context.InsertAll(new[] {
            new NodeMessage (
                machineId,
                messageId,
                command
            ),
        });

        var commandRequest = new NodeCommandGet(machineId);
        var auth = new TestEndpointAuthorization(RequestType.Agent, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCommands(LoggerProvider.CreateLogger<AgentCommands>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.FromJson("GET", commandRequest));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pendingNodeCommand = BodyAs<PendingNodeCommand>(result);
        pendingNodeCommand.Envelope.Should().NotBeNull();
        pendingNodeCommand.Envelope?.Command.Should().BeEquivalentTo(command);
        pendingNodeCommand.Envelope?.MessageId.Should().Be(messageId);
    }
}
