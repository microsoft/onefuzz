using Azure.Data.Tables;
using Azure.ResourceManager.Storage.Models;
using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;


record NodeCommandStopIfFree { }

record StopNodeCommand{}

record StopTaskNodeCommand{
    Guid TaskId;
}

record NodeCommandAddSshKey{
    string PublicKey;
}

record NodeCommand
{
    StopNodeCommand? Stop;
    StopTaskNodeCommand? StopTask;
    NodeCommandAddSshKey? AddSshKey;
    NodeCommandStopIfFree? StopIfFree;
}

enum NodeTaskState
{
    init,
    setting_up,
    running,
}

record NodeTasks
{
    Guid MachineId;
    Guid TaskId;
    NodeTaskState State = NodeTaskState.init;

}

enum NodeState
{
    init,
    free,
    setting_up,
    rebooting,
    ready,
    busy,
    done,
    shutdown,
    halt,
}


public record Error (ErrorCode Code, string[]? Errors = null);

public record UserInfo (Guid? ApplicationId, Guid? ObjectId, String? Upn);