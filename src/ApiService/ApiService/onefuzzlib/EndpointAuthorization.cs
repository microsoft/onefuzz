using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public interface IEndpointAuthorization {
    Async.Task<HttpResponseData> CallIfAgent(
        HttpRequestData req,
        Func<HttpRequestData, Async.Task<HttpResponseData>> method)
        => CallIf(req, method, allowAgent: true);

    Async.Task<HttpResponseData> CallIfUser(
        HttpRequestData req,
        Func<HttpRequestData, Async.Task<HttpResponseData>> method)
        => CallIf(req, method, allowUser: true);

    Async.Task<HttpResponseData> CallIf(
        HttpRequestData req,
        Func<HttpRequestData, Async.Task<HttpResponseData>> method,
        bool allowUser = false,
        bool allowAgent = false);
}

public class EndpointAuthorization : IEndpointAuthorization {
    private readonly IOnefuzzContext _context;
    private readonly ILogTracer _log;

    public EndpointAuthorization(IOnefuzzContext context, ILogTracer log) {
        _context = context;
        _log = log;
    }

    public async Async.Task<HttpResponseData> CallIf(HttpRequestData req, Func<HttpRequestData, Async.Task<HttpResponseData>> method, bool allowUser = false, bool allowAgent = false) {
        var tokenResult = await _context.UserCredentials.ParseJwtToken(req);

        if (!tokenResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, tokenResult.ErrorV, "token verification", HttpStatusCode.Unauthorized);
        }
        var token = tokenResult.OkV!;

        if (await IsUser(token)) {
            if (!allowUser) {
                return await Reject(req, token);
            }

            var access = CheckAccess(req);
            if (!access.IsOk) {
                return await _context.RequestHandling.NotOk(req, access.ErrorV, "access control", HttpStatusCode.Unauthorized);
            }
        }


        if (await IsAgent(token) && !allowAgent) {
            return await Reject(req, token);
        }

        return await method(req);
    }

    public async Async.Task<bool> IsUser(UserInfo tokenData) {
        return !await IsAgent(tokenData);
    }

    public async Async.Task<HttpResponseData> Reject(HttpRequestData req, UserInfo token) {
        _log.Error(
            $"reject token. url:{req.Url} token:{token} body:{await req.ReadAsStringAsync()}"
        );

        return await _context.RequestHandling.NotOk(
            req,
            new Error(
                ErrorCode.UNAUTHORIZED,
                new string[] { "Unrecognized agent" }
            ),
            "token verification",
            HttpStatusCode.Unauthorized
        );
    }

    public OneFuzzResultVoid CheckAccess(HttpRequestData req) {
        throw new NotImplementedException();
    }

    public async Async.Task<bool> IsAgent(UserInfo tokenData) {
        if (tokenData.ObjectId != null) {
            var scalesets = _context.ScalesetOperations.GetByObjectId(tokenData.ObjectId.Value);
            if (await scalesets.AnyAsync()) {
                return true;
            }

            var principalId = _context.Creds.GetScalesetPrincipalId();
            return principalId == tokenData.ObjectId;
        }

        if (!tokenData.ApplicationId.HasValue) {
            return false;
        }

        var pools = _context.PoolOperations.GetByClientId(tokenData.ApplicationId.Value);
        if (await pools.AnyAsync()) {
            return true;
        }

        return false;
    }
}
