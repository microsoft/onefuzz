using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public record FunctionInfo(string Name, string ResourceGroup, string? SlotName);
public class TestHooks {

    private readonly ILogTracer _log;
    private readonly IConfigOperations _configOps;
    private readonly IEvents _events;
    private readonly IServiceConfig _config;
    private readonly ISecretsOperations _secretOps;
    private readonly ILogAnalytics _logAnalytics;

    public TestHooks(ILogTracer log, IConfigOperations configOps, IEvents events, IServiceConfig config, ISecretsOperations secretOps, ILogAnalytics logAnalytics) {
        _log = log;
        _configOps = configOps;
        _events = events;
        _config = config;
        _secretOps = secretOps;
        _logAnalytics = logAnalytics;
    }

    [Function("Info")]
    public async Task<HttpResponseData> Info([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/info")] HttpRequestData req) {
        _log.Info("Creating function info response");
        var response = req.CreateResponse();
        FunctionInfo info = new(
                $"{_config.OneFuzzInstanceName}",
                $"{_config.OneFuzzResourceGroup}",
                Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME"));

        _log.Info("Returning function info");
        await response.WriteAsJsonAsync(info);
        _log.Info("Returned function info");
        return response;
    }



    [Function("GetKeyvaultAddress")]
    public async Task<HttpResponseData> GetKeyVaultAddress([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/secrets/keyvaultaddress")] HttpRequestData req) {
        _log.Info("Getting keyvault address");
        var addr = _secretOps.GetKeyvaultAddress();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(addr);
        return resp;
    }

    [Function("SaveToKeyvault")]
    public async Task<HttpResponseData> SaveToKeyvault([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/secrets/keyvault")] HttpRequestData req) {
        var s = await req.ReadAsStringAsync();
        var secretData = JsonSerializer.Deserialize<SecretData<string>>(s!, EntityConverter.GetJsonSerializerOptions());
        if (secretData is null) {
            _log.Error("Secret data is null");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        } else {
            _log.Info($"Saving secret data in the keyvault");
            var r = await _secretOps.SaveToKeyvault(secretData);
            var addr = _secretOps.GetKeyvaultAddress();
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(addr);
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
        var d = await _secretOps.GetSecretStringValue(new SecretData<string>(qs["SecretName"]));

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(d);
        return resp;
    }


    [Function("GetWorkspaceId")]
    public async Task<HttpResponseData> GetWorkspaceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/logAnalytics/workspaceId")] HttpRequestData req) {
        var id = _logAnalytics.GetWorkspaceId();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(id);
        return resp;
    }



    [Function("GetMonitorSettings")]
    public async Task<HttpResponseData> GetMonitorSettings([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/logAnalytics/monitorSettings")] HttpRequestData req) {
        var settings = await _logAnalytics.GetMonitorSettings();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(settings);
        return resp;
    }

}
