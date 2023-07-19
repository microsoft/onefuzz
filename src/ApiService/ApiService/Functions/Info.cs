using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Info {
    private readonly IOnefuzzContext _context;
    private readonly Lazy<Async.Task<InfoResponse>> _response;

    public Info(IOnefuzzContext context) {
        _context = context;

        // TODO: this isn’t actually shared between calls at the moment,
        // this needs to be placed into a class that can be registered into the
        // DI container and shared amongst instances.
        //
        // However, we need to be careful about auth and caching between different
        // credentials.
        _response = new Lazy<Async.Task<InfoResponse>>(async () => {
            var config = _context.ServiceConfiguration;

            var resourceGroup = _context.Creds.GetBaseResourceGroup();
            var subscription = _context.Creds.GetSubscription();
            var region = await _context.Creds.GetBaseRegion();

            var asm = Assembly.GetExecutingAssembly();
            var gitVersion = ReadResource(asm, "ApiService.onefuzzlib.git.version");
            var buildId = ReadResource(asm, "ApiService.onefuzzlib.build.id");
            var versionString = context.ServiceConfiguration.OneFuzzVersion;

            var dotnetVersionString = $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier})";

            return new InfoResponse(
                ResourceGroup: resourceGroup,
                Subscription: subscription,
                Region: region,
                Versions: new Dictionary<string, InfoVersion> {
                    { "onefuzz", new(gitVersion, buildId, versionString) },
                    { "dotnet", new("", "", dotnetVersionString) },
                },
                InstanceId: await _context.Containers.GetInstanceId(),
                InsightsAppid: config.ApplicationInsightsAppId,
                InsightsInstrumentationKey: config.ApplicationInsightsInstrumentationKey);
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    private static string ReadResource(Assembly asm, string resourceName) {
        using var r = asm.GetManifestResourceStream(resourceName);
        if (r is null) {
            return "unknown";
        }

        using var sr = new StreamReader(r);
        return sr.ReadToEnd().Trim();
    }

    [Function("Info")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET")] HttpRequestData req)
        => await RequestHandling.Ok(req, await _response.Value);
}
