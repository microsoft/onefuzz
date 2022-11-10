using System.Net;
using System.Net.Http;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Graph;

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

    Async.Task<OneFuzzResultVoid> CheckRequireAdmins(HttpRequestData req);
}

public class EndpointAuthorization : IEndpointAuthorization {
    private readonly IOnefuzzContext _context;
    private readonly ILogTracer _log;
    private readonly GraphServiceClient _graphClient;

    private static readonly HashSet<string> AgentRoles = new HashSet<string> { "UnmamagedNode", "ManagedNode" };

    public EndpointAuthorization(IOnefuzzContext context, ILogTracer log, GraphServiceClient graphClient) {
        _context = context;
        _log = log;
        _graphClient = graphClient;
    }

    public virtual async Async.Task<HttpResponseData> CallIf(HttpRequestData req, Func<HttpRequestData, Async.Task<HttpResponseData>> method, bool allowUser = false, bool allowAgent = false) {
        var tokenResult = await _context.UserCredentials.ParseJwtToken(req);

        if (!tokenResult.IsOk) {
            return await _context.RequestHandling.NotOk(req, tokenResult.ErrorV, "token verification", HttpStatusCode.Unauthorized);
        }

        var token = tokenResult.OkV.UserInfo;
        if (await IsUser(tokenResult.OkV)) {
            if (!allowUser) {
                return await Reject(req, tokenResult.OkV.UserInfo);
            }

            var access = await CheckAccess(req);
            if (!access.IsOk) {
                return await _context.RequestHandling.NotOk(req, access.ErrorV, "access control", HttpStatusCode.Unauthorized);
            }
        }

        if (await IsAgent(tokenResult.OkV) && !allowAgent) {
            return await Reject(req, tokenResult.OkV.UserInfo);
        }

        return await method(req);
    }

    public async Async.Task<bool> IsUser(UserAuthInfo tokenData) {
        return !await IsAgent(tokenData);
    }

    public async Async.Task<HttpResponseData> Reject(HttpRequestData req, UserInfo token) {
        var body = await req.ReadAsStringAsync();
        _log.Error($"reject token. url:{req.Url:Tag:Url} token:{token:Tag:Token} body:{body:Tag:Body}");

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

    public async Async.Task<OneFuzzResultVoid> CheckRequireAdmins(HttpRequestData req) {
        var tokenResult = await _context.UserCredentials.ParseJwtToken(req);
        if (!tokenResult.IsOk) {
            return tokenResult.ErrorV;
        }

        var config = await _context.ConfigOperations.Fetch();
        if (config is null) {
            return new Error(
                Code: ErrorCode.INVALID_CONFIGURATION,
                Errors: new string[] { "no instance configuration found " });
        }

        return CheckRequireAdminsImpl(config, tokenResult.OkV.UserInfo);
    }

    private static OneFuzzResultVoid CheckRequireAdminsImpl(InstanceConfig config, UserInfo userInfo) {
        // When there are no admins in the `admins` list, all users are considered
        // admins.  However, `require_admin_privileges` is still useful to protect from
        // mistakes.
        //
        // To make changes while still protecting against accidental changes to
        // pools, do the following:
        //
        // 1. set `require_admin_privileges` to `False`
        // 2. make the change
        // 3. set `require_admin_privileges` to `True`

        if (config.RequireAdminPrivileges == false) {
            return OneFuzzResultVoid.Ok;
        }

        if (config.Admins is null) {
            return new Error(
                Code: ErrorCode.UNAUTHORIZED,
                Errors: new string[] { "pool modification disabled " });
        }

        if (userInfo.ObjectId is Guid objectId) {
            if (config.Admins.Contains(objectId)) {
                return OneFuzzResultVoid.Ok;
            }

            return new Error(
                Code: ErrorCode.UNAUTHORIZED,
                Errors: new string[] { "not authorized to manage instance" });
        } else {
            return new Error(
                Code: ErrorCode.UNAUTHORIZED,
                Errors: new string[] { "user had no Object ID" });
        }
    }

    public async Async.Task<OneFuzzResultVoid> CheckAccess(HttpRequestData req) {
        var instanceConfig = await _context.ConfigOperations.Fetch();

        var rules = GetRules(instanceConfig);
        if (rules is null) {
            return OneFuzzResultVoid.Ok;
        }

        var path = req.Url.AbsolutePath;
        var rule = rules.GetMatchingRules(new HttpMethod(req.Method), path);
        if (rule is null) {
            return OneFuzzResultVoid.Ok;
        }

        var memberId = Guid.Parse(req.Headers.GetValues("x-ms-client-principal-id").Single());
        try {
            var membershipChecker = CreateGroupMembershipChecker(instanceConfig);
            var allowed = await membershipChecker.IsMember(rule.AllowedGroupsIds, memberId);
            if (!allowed) {
                _log.Error($"unauthorized access: {memberId:Tag:MemberId} is not authorized to access {path:Tag:Path}");
                return new Error(
                    Code: ErrorCode.UNAUTHORIZED,
                    Errors: new string[] { "not approved to use this endpoint" });
            } else {
                return OneFuzzResultVoid.Ok;
            }
        } catch (Exception ex) {
            return new Error(
                Code: ErrorCode.UNAUTHORIZED,
                Errors: new string[] { "unable to interact with graph", ex.Message });
        }
    }

    private GroupMembershipChecker CreateGroupMembershipChecker(InstanceConfig config) {
        if (config.GroupMembership is not null) {
            return new StaticGroupMembership(config.GroupMembership);
        }

        return new AzureADGroupMembership(_graphClient);
    }

    private static RequestAccess? GetRules(InstanceConfig config) {
        var accessRules = config?.ApiAccessRules;
        if (accessRules is not null) {
            return RequestAccess.Build(accessRules);
        }

        return null;
    }


    public async Async.Task<bool> IsAgent(UserAuthInfo authInfo) {
        if (!AgentRoles.Overlaps(authInfo.Roles)) {
            return false;
        }

        var tokenData = authInfo.UserInfo;

        if (tokenData.ObjectId != null) {
            var scalesets = _context.ScalesetOperations.GetByObjectId(tokenData.ObjectId.Value);
            if (await scalesets.AnyAsync()) {
                return true;
            }

            var principalId = await _context.Creds.GetScalesetPrincipalId();
            return principalId == tokenData.ObjectId;
        }

        if (!tokenData.ApplicationId.HasValue) {
            return false;
        }

        var pools = _context.PoolOperations.GetByClientId(tokenData.ApplicationId.Value);
        if (await pools.AnyAsync()) {
            return true;
        }

        // if (tokenData.Roles.Contains("unmanagedNode")) {
        //     return true;
        // }

        return false;
    }
}
