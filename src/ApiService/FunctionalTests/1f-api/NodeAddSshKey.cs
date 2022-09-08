using System.Text.Json.Nodes;
using Xunit.Abstractions;


namespace FunctionalTests {
    public class NodeAddSshKeyApi : ApiBase {

        public NodeAddSshKeyApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
            base(endpoint, "/api/node_add_ssh_key", request, output) {
        }

        public async Task<BooleanResult> Post(Guid machineId, string publicSshKey) {
            var n = new JsonObject()
                .AddV("machine_id", machineId)
                .AddV("public_key", publicSshKey);

            var r = await Post(n);
            return Return<BooleanResult>(r);
        }
    }
}
