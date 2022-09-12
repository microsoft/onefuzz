using System.Net;
using System.Web;
using Xunit.Abstractions;

namespace FunctionalTests {
    public class DownloadApi : ApiBase {

        public DownloadApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
            base(endpoint, "/api/Download", request, output) {
        }

        public async Task<Result<Stream, (HttpStatusCode, Error?)>> Get(string? container = null, string? filename = null) {
            var n = HttpUtility.ParseQueryString(string.Empty);
            if (container is not null)
                n.Add("container", container);
            if (filename is not null)
                n.Add("filename", filename);
            return await QueryGet(n.ToString());
        }
    }
}
