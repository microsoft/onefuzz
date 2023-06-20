using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service;
#if DEBUG
public record FunctionInfo(string Name, string ResourceGroup, string? SlotName);
public class TestHooks {

    private readonly ILogger _log;
    private readonly IConfigOperations _configOps;
    private readonly IEvents _events;
    private readonly IServiceConfig _config;
    private readonly ISecretsOperations _secretOps;
    private readonly ILogAnalytics _logAnalytics;

    public TestHooks(ILogger<TestHooks> log, IConfigOperations configOps, IEvents events, IServiceConfig config, ISecretsOperations secretOps, ILogAnalytics logAnalytics) {
        _log = log;
        _configOps = configOps;
        _events = events;
        _config = config;
        _secretOps = secretOps;
        _logAnalytics = logAnalytics;
    }

    [Function("_Info")]
    public async Task<HttpResponseData> Info([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/info")] HttpRequestData req) {
        _log.LogInformation("Creating function info response");
        var response = req.CreateResponse();
        FunctionInfo info = new(
                $"{_config.OneFuzzInstanceName}",
                $"{_config.OneFuzzResourceGroup}",
                Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME"));

        _log.LogInformation("Returning function info");
        await response.WriteAsJsonAsync(info);
        _log.LogInformation("Returned function info");
        return response;
    }


    [Function("SaveToKeyvault")]
    public async Task<HttpResponseData> SaveToKeyvault([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/secrets/keyvault")] HttpRequestData req) {
        var s = await req.ReadAsStringAsync();
        var secretData = JsonSerializer.Deserialize<SecretData<string>>(s!, EntityConverter.GetJsonSerializerOptions());
        if (secretData is null) {
            _log.LogError("Secret data is null");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        } else {
            _log.LogInformation("Saving secret data in the keyvault");
            var r = await _secretOps.StoreSecretData(secretData);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync((r.Secret as SecretAddress<string>)?.Url);
            return resp;
        }
    }

    [Function("GetSecretStringValue")]
    public async Task<HttpResponseData> GetSecretStringValue([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/secrets/keyvault")] HttpRequestData req) {
        var queryComponents = req.Url.GetComponents(UriComponents.Query, UriFormat.UriEscaped).Split("&");

        var q =
            from cs in queryComponents
            where !string.IsNullOrEmpty(cs)
            let i = cs.IndexOf('=')
            select new KeyValuePair<string, string>(Uri.UnescapeDataString(cs.Substring(0, i)), Uri.UnescapeDataString(cs.Substring(i + 1)));

        var qs = new Dictionary<string, string>(q);
        var d = await _secretOps.GetSecretValue(new SecretValue<string>(qs["SecretName"]));

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(d);
        return resp;
    }

}
#endif
