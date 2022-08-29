using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

class Node {
    JsonElement _e;

    public Node() { }

    public Node(JsonElement e) => _e = e;

    public string PoolName => _e.GetProperty("pool_name").GetString()!;

    public Guid MachineId => _e.GetProperty("machine_id").GetGuid();

    public Guid? PoolId => _e.GetProperty("pool_id").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("pool_id").GetGuid();

    public string Version => _e.GetProperty("version").GetString()!;

    public DateTimeOffset? HeartBeat => _e.GetProperty("heart_beat").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("heart_beat").GetDateTimeOffset();

    public DateTimeOffset? InitializedAt => _e.GetProperty("initialized_at").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("initialized_at").GetDateTimeOffset();

    public string State => _e.GetProperty("state").GetString()!;

    public Guid? ScalesetId => _e.GetProperty("scaleset_id").ValueKind == JsonValueKind.Null ? null : _e.GetProperty("scaleset_id").GetGuid();
    public bool ReimageRequested => _e.GetProperty("reimage_requested").GetBoolean();
    public bool DeleteRequested => _e.GetProperty("delete_requested").GetBoolean();
    public bool DebugKeepNode => _e.GetProperty("debug_keep_node").GetBoolean();
}


class NodeApi : ApiBase<Node> {

    public NodeApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Node", request, output) {
    }
    public override Node Convert(JsonElement e) { return new Node(e); }

    public async Task<JsonElement> Update(Guid machineId, bool? debugKeepNode = null) {
        var j = new JsonObject();
        if (debugKeepNode is not null)
            j.Add("debug_keep_node", JsonValue.Create(debugKeepNode));

        j.Add("machine_id", JsonValue.Create(machineId));
        return await Post(j);
    }
    public async Task<Result<IEnumerable<Node>, Error>> Get(Guid? machineId = null, List<string>? state = null, Guid? scalesetId = null, string? poolName = null) {
        var j = new JsonObject();
        if (machineId is not null)
            j.Add("machine_id", JsonValue.Create(machineId));
        if (state is not null) {
            var states = new JsonArray(state.Select(s => JsonValue.Create(s)).ToArray());
            j.Add("state", JsonValue.Create(states));
        }
        if (scalesetId is not null)
            j.Add("scaleset_id", JsonValue.Create(scalesetId));

        if (poolName is not null)
            j.Add("pool_name", JsonValue.Create(poolName));

        return IEnumerableResult(await Get(j));
    }

    public async Task<JsonElement> Patch(Guid machineId) {
        var j = new JsonObject();
        j.Add("machine_id", JsonValue.Create(machineId));
        return await Patch(j);
    }

    public async Task<bool> Delete(Guid machineId) {
        var j = new JsonObject();
        j.Add("machine_id", JsonValue.Create(machineId));
        return DeleteResult(await Delete(j));
    }

    public async Task<Node> WaitWhile(Guid id, Func<Node, bool> wait) {
        var currentState = "";
        Node node;
        do {
            await Task.Delay(TimeSpan.FromSeconds(10.0));
            var sc = await Get(machineId: id);
            Assert.True(sc.IsOk, $"failed to get node with id: {id} due to {sc.ErrorV}");
            node = sc.OkV!.First();

            if (currentState != node.State) {
                _output.WriteLine($"Node is in {node.State}");
                currentState = node.State;
            }
        } while (wait(node));

        return node;
    }

}
