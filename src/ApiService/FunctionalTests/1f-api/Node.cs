using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

public class Node : IFromJsonElement<Node> {
    readonly JsonElement _e;

    public Node(JsonElement e) => _e = e;

    public string PoolName => _e.GetStringProperty("pool_name");

    public Guid MachineId => _e.GetGuidProperty("machine_id");

    public Guid? PoolId => _e.GetNullableGuidProperty("pool_id");

    public string Version => _e.GetStringProperty("version");

    public DateTimeOffset? HeartBeat => _e.GetNullableDateTimeOffsetProperty("heart_beat");

    public DateTimeOffset? InitializedAt => _e.GetNullableDateTimeOffsetProperty("initialized_at");

    public string State => _e.GetStringProperty("state");

    public string? ScalesetId => _e.GetNullableStringProperty("scaleset_id");
    public bool ReimageRequested => _e.GetBoolProperty("reimage_requested");
    public bool DeleteRequested => _e.GetBoolProperty("delete_requested");
    public bool DebugKeepNode => _e.GetBoolProperty("debug_keep_node");

    public static Node Convert(JsonElement e) => new(e);
}


public class NodeApi : ApiBase {

    public NodeApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/Node", request, output) {
    }

    public async Task<BooleanResult> Update(Guid machineId, bool? debugKeepNode = null) {
        var j = new JsonObject()
            .AddIfNotNullV("debug_keep_node", debugKeepNode)
            .AddV("machine_id", machineId);
        return Return<BooleanResult>(await Post(j));
    }
    public async Task<Result<IEnumerable<Node>, Error>> Get(Guid? machineId = null, IEnumerable<string>? state = null, string? scalesetId = null, string? poolName = null) {
        var j = new JsonObject()
            .AddIfNotNullV("machine_id", machineId)
            .AddIfNotNullEnumerableV("state", state)
            .AddIfNotNullV("scaleset_id", scalesetId)
            .AddIfNotNullV("pool_name", poolName);

        return IEnumerableResult<Node>(await Get(j));
    }

    public async Task<BooleanResult> Patch(Guid machineId) {
        var j = new JsonObject().AddV("machine_id", machineId);
        return Return<BooleanResult>(await Patch(j));
    }

    public async Task<BooleanResult> Delete(Guid machineId) {
        var j = new JsonObject().AddV("machine_id", machineId);
        return Return<BooleanResult>(await Delete(j));
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
