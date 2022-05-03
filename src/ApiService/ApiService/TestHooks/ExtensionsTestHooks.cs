using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;


#if DEBUG
namespace ApiService.TestHooks {

    public class ExtensionsTestHooks {

        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IExtensions _extensions;

        public ExtensionsTestHooks(ILogTracer log, IConfigOperations configOps, IExtensions extensions) {
            _log = log.WithTag("TestHooks", nameof(ExtensionsTestHooks));
            _configOps = configOps;
            _extensions = extensions;
        }

        [Function("GenericExtensionsHook")]
        public async Task<HttpResponseData> GenericExtensions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/extensions/genericExtensions")] HttpRequestData req) {
            _log.Info("Get Generic extensions");

            var query = UriExtension.GetQueryComponents(req.Url);

            Os os;
            if (query["os"].ToLowerInvariant() == "windows") {
                os = Os.Windows;
            } else if (query["os"].ToLowerInvariant() == "linux") {
                os = Os.Linux;
            } else {
                var err = req.CreateResponse(HttpStatusCode.BadRequest);
                await err.WriteAsJsonAsync(new { error = $"unsupported os {query["os"]}" });
                return err;
            }

            var ext = await (_extensions as Extensions)!.GenericExtensions(query["region"], os);
            var resp = req.CreateResponse(HttpStatusCode.OK);

            await resp.WriteAsJsonAsync(ext);

            return resp;
        }



    }
}

#endif
