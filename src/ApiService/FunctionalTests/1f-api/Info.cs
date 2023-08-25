using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests {

    public class InfoVersion : IFromJsonElement<InfoVersion> {
        readonly JsonElement _e;

        public InfoVersion(JsonElement e) => _e = e;
        public static InfoVersion Convert(JsonElement e) => new(e);

        public string Git => _e.GetProperty("git").GetString()!;
        public string Build => _e.GetProperty("build").GetString()!;
        public string Version => _e.GetProperty("version").GetString()!;
    }


    public class InfoResponse : IFromJsonElement<InfoResponse> {
        readonly JsonElement _e;

        public InfoResponse(JsonElement e) => _e = e;

        public static InfoResponse Convert(JsonElement e) => new(e);

        public string ResourceGroup => _e.GetStringProperty("resource_group")!;
        public string Region => _e.GetStringProperty("region")!;
        public string Subscription => _e.GetStringProperty("subscription")!;
        public IDictionary<string, InfoVersion> Versions => _e.GetDictProperty<InfoVersion>("versions");
    }


    sealed class InfoApi : ApiBase {

        public InfoApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
            base(endpoint, "/api/Info", request, output) {
        }

        public async Task<Result<InfoResponse, Error>> Get() {
            var n = new JsonObject();
            var res = await Get(n);
            return Result<InfoResponse>(res);
        }
    }
}
