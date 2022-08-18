﻿
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
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

    public TestEndpointAuthorization(RequestType type, ILogTracer log, IOnefuzzContext context) : base(context, log) {
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
            new Error(
                ErrorCode.UNAUTHORIZED,
                new string[] { "Unrecognized agent" }
            ),
            "token verification",
            HttpStatusCode.Unauthorized
        );
    }
}
