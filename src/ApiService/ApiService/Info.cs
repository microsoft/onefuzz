using System.Threading;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class Info {
    private readonly IOnefuzzContext _context;
    private readonly IEndpointAuthorization _auth;
    private readonly Lazy<Async.Task<InfoResponse>> _response;

    public Info(IEndpointAuthorization auth, IOnefuzzContext context) {
        _context = context;
        _auth = auth;

        _response = new Lazy<Async.Task<InfoResponse>>(async () => {
            var config = _context.ServiceConfiguration;

            var resourceGroup = _context.Creds.GetBaseResourceGroup();
            var subscription = _context.Creds.GetSubscription();
            var region = await _context.Creds.GetBaseRegion();

            return new InfoResponse(
                ResourceGroup: resourceGroup,
                Subscription: subscription,
                Region: region,
                Versions: new Dictionary<string, InfoVersion> { { "onefuzz", new("TODO", "TODO", config.OneFuzzVersion) } },
                InstanceId: await _context.Containers.GetInstanceId(),
                InsightsAppid: config.ApplicationInsightsAppId,
                InsightsInstrumentationKey: config.ApplicationInsightsInstrumentationKey);
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    private async Async.Task<HttpResponseData> GetResponse(HttpRequestData req)
        => await RequestHandling.Ok(req, await _response.Value);

    // [Function("Info")]
    public Async.Task<HttpResponseData> Run([HttpTrigger("GET")] HttpRequestData req)
        => _auth.CallIfUser(req, GetResponse);
}
