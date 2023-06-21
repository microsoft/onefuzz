﻿using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;

#if DEBUG
namespace ApiService.TestHooks {

    public class ExtensionsTestHooks {

        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly IExtensions _extensions;

        public ExtensionsTestHooks(ILogger<ExtensionsTestHooks> log, IConfigOperations configOps, IExtensions extensions) {
            _log = log;
            _log.AddTag("TestHooks", nameof(ExtensionsTestHooks));
            _configOps = configOps;
            _extensions = extensions;
        }

        [Function("GenericExtensionsHook")]
        public async Task<HttpResponseData> GenericExtensions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/extensions/genericExtensions")] HttpRequestData req) {
            _log.LogInformation("Get Generic extensions");

            var query = UriExtension.GetQueryComponents(req.Url);
            Os os = Enum.Parse<Os>(query["os"]);

            var ext = await (_extensions as Extensions)!.GenericExtensions(query["region"], os);
            var resp = req.CreateResponse(HttpStatusCode.OK);

            await resp.WriteAsJsonAsync(ext);

            return resp;
        }



    }
}

#endif
