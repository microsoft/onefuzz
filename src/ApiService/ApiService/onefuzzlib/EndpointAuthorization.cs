using System.Net.Http;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
namespace Microsoft.OneFuzz.Service;

public record UserAuthInfo(UserInfo UserInfo, List<string> Roles);

public interface IEndpointAuthorization {
    Async.Task<OneFuzzResultVoid> CheckRequireAdmins(UserAuthInfo authInfo);
    Async.Task<(bool, string)> IsAgent(UserAuthInfo authInfo);
    Async.Task<OneFuzzResultVoid> CheckAccess(HttpRequestData req);
}


public class EndpointAuthorization : IEndpointAuthorization {
    private readonly IOnefuzzContext _context;
    private readonly ILogger _log;
    private readonly GraphServiceClient _graphClient;
    private static readonly IReadOnlySet<string> _agentRoles = new HashSet<string>() { "UnmanagedNode", "ManagedNode" };

    public EndpointAuthorization(IOnefuzzContext context, ILogger<EndpointAuthorization> log, GraphServiceClient graphClient) {
        _context = context;
        _log = log;
        _graphClient = graphClient;
    }

    public async Async.Task<OneFuzzResultVoid> CheckRequireAdmins(UserAuthInfo authInfo) {
        var config = await _context.ConfigOperations.Fetch();
        if (config is null) {
            return Error.Create(
                ErrorCode.INVALID_CONFIGURATION,
                "no instance configuration found ");
        }

        return CheckRequireAdminsImpl(config, authInfo.UserInfo);
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
                _log.LogError("unauthorized access: {MemberId} is not authorized to access {Path}", memberId, path);
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
        if (!_agentRoles.Overlaps(authInfo.Roles)) {
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
