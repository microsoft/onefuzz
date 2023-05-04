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
    private static readonly HashSet<string> AgentRoles = new HashSet<string> { "UnmanagedNode", "ManagedNode" };

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

        var (isAgent, reason) = await IsAgent(tokenResult.OkV);

        if (!isAgent) {
            if (!allowUser) {
                return await Reject(req, token, "endpoint not allowed for users");
            }

            var access = await CheckAccess(req);
            if (!access.IsOk) {
                return await _context.RequestHandling.NotOk(req, access.ErrorV, "access control", HttpStatusCode.Unauthorized);
            }
        }


        if (isAgent && !allowAgent) {
            return await Reject(req, token, reason);
        }

        return await method(req);
    }


    public async Async.Task<HttpResponseData> Reject(HttpRequestData req, UserInfo token, String? reason = null) {
        var body = await req.ReadAsStringAsync();
        _log.Error($"reject token. reason:{reason} url:{req.Url:Tag:Url} token:{token:Tag:Token} body:{body:Tag:Body}");

        return await _context.RequestHandling.NotOk(
            req,
           Error.Create(
                ErrorCode.UNAUTHORIZED,
                reason ?? "Unrecognized agent"
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
            return Error.Create(
                ErrorCode.INVALID_CONFIGURATION,
                "no instance configuration found ");
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
            return Error.Create(ErrorCode.UNAUTHORIZED, "pool modification disabled ");
        }

        if (userInfo.ObjectId is Guid objectId) {
            if (config.Admins.Contains(objectId)) {
                return OneFuzzResultVoid.Ok;
            }

            return Error.Create(ErrorCode.UNAUTHORIZED, "not authorized to manage instance");
        } else {
            return Error.Create(ErrorCode.UNAUTHORIZED, "user had no Object ID");
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
                return Error.Create(ErrorCode.UNAUTHORIZED, "not approved to use this endpoint");
            } else {
                return OneFuzzResultVoid.Ok;
            }
        } catch (Exception ex) {
            return Error.Create(ErrorCode.UNAUTHORIZED, "unable to interact with graph", ex.Message);
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


    public async Async.Task<(bool, string)> IsAgent(UserAuthInfo authInfo) {
        if (!AgentRoles.Overlaps(authInfo.Roles)) {
            return (false, "no agent role");
        }

        var tokenData = authInfo.UserInfo;

        if (tokenData.ObjectId != null) {
            var scalesets = _context.ScalesetOperations.GetByObjectId(tokenData.ObjectId.Value);
            if (await scalesets.AnyAsync()) {
                return (true, string.Empty);
            }

            var principalId = await _context.Creds.GetScalesetPrincipalId();
            if (principalId == tokenData.ObjectId) {
                return (true, string.Empty);
            }
        }

        if (!tokenData.ObjectId.HasValue) {
            return (false, "no object id in token");
        }

        var pools = _context.PoolOperations.GetByObjectId(tokenData.ObjectId.Value);
        if (await pools.AnyAsync()) {
            return (true, string.Empty);
        }

        return (false, "no matching scaleset or pool");
    }
}
