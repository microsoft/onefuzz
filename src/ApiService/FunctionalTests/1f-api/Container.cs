using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests {

    public class ContainerInfo : IFromJsonElement<ContainerInfo> {
        readonly JsonElement _e;
        public ContainerInfo(JsonElement e) => _e = e;
        public static ContainerInfo Convert(JsonElement e) => new(e);
        public string Name => _e.GetStringProperty("name");
        public IDictionary<string, string>? Metadata => _e.GetNullableStringDictProperty("metadata");
        public Uri SasUrl => new Uri(_e.GetStringProperty("sas_url"));
    }

    public class ContainerApi : ApiBase {

        public ContainerApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
            base(endpoint, "/api/Containers", request, output) {
        }

        public async Task<Result<IEnumerable<ContainerInfo>, Error>> Get(string? name = null) {
            var n = new JsonObject()
                .AddIfNotNullV("name", name);

            var res = await Get(n);
            return IEnumerableResult<ContainerInfo>(res);
        }

        public async Task<BooleanResult> Delete(string name, IDictionary<string, string>? metadata = null) {
            var n = new JsonObject()
                .AddV("name", name)
                .AddIfNotNullV("metadata", metadata);

            return Return<BooleanResult>(await Delete(n));
        }

        public async Task<Result<ContainerInfo, Error>> Post(string name, IDictionary<string, string>? metadata = null) {
            var n = new JsonObject()
                .AddV("name", name)
                .AddIfNotNullV("metadata", metadata);
            var res = await Post(n);
            return Result<ContainerInfo>(res);
        }
    }

}
