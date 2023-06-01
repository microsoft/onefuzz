
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
namespace IntegrationTests.Fakes;

public enum RequestType {
    NoAuthorization,
    User,
    Agent,
}

sealed class TestEndpointAuthorization : EndpointAuthorization {
    private readonly RequestType _type;
    private readonly IOnefuzzContext _context;

    public TestEndpointAuthorization(RequestType type, ILogger<EndpointAuthorization> log, IOnefuzzContext context)
        : base(context, log, null! /* not needed for test */) {
        _type = type;
        _context = context;
    }

    public override Task<HttpResponseData> CallIf(
        HttpRequestData req,
        Func<HttpRequestData, Task<HttpResponseData>> method,
        bool allowUser = false,
        bool allowAgent = false) {

        if ((_type == RequestType.User && allowUser) ||
            (_type == RequestType.Agent && allowAgent)) {
            return method(req);
        }

        return _context.RequestHandling.NotOk(
            req,
            Error.Create(
                ErrorCode.UNAUTHORIZED,
                "Unrecognized agent"
            ),
            "token verification",
            HttpStatusCode.Unauthorized
        );
    }
}
