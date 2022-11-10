using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.IdentityModel.Tokens;


namespace Microsoft.OneFuzz.Service;

public interface IUserCredentials {
    public string? GetBearerToken(HttpRequestData req);
    public string? GetAuthToken(HttpRequestData req);
    public Task<OneFuzzResult<UserAuthInfo>> ParseJwtToken(HttpRequestData req);
}

public record UserAuthInfo(UserInfo UserInfo, List<string> Roles);

public class UserCredentials : IUserCredentials {
    ILogTracer _log;
    IConfigOperations _instanceConfig;
    private JwtSecurityTokenHandler _tokenHandler;

    public UserCredentials(ILogTracer log, IConfigOperations instanceConfig) {
        _log = log;
        _instanceConfig = instanceConfig;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public string? GetBearerToken(HttpRequestData req) {
        if (!req.Headers.TryGetValues("Authorization", out var authHeader) || authHeader.IsNullOrEmpty()) {
            return null;
        } else {
            var auth = AuthenticationHeaderValue.Parse(authHeader.First());
            return auth.Scheme.ToLower() switch {
                "bearer" => auth.Parameter,
                _ => null,
            };
        }
    }

    public string? GetAuthToken(HttpRequestData req) {
        var token = GetBearerToken(req);
        if (token is not null) {
            return token;
        } else {
            if (!req.Headers.TryGetValues("x-ms-token-aad-id-token", out var tokenHeader) || tokenHeader.IsNullOrEmpty()) {
                return null;
            } else {
                return tokenHeader.First();
            }
        }
    }

    async Task<OneFuzzResult<string[]>> GetAllowedTenants() {
        var r = await _instanceConfig.Fetch();
        var allowedAddTenantsQuery =
            from t in r.AllowedAadTenants
            select $"https://sts.windows.net/{t}/";

        return OneFuzzResult<string[]>.Ok(allowedAddTenantsQuery.ToArray());
    }

    public virtual async Task<OneFuzzResult<UserAuthInfo>> ParseJwtToken(HttpRequestData req) {


        var authToken = GetAuthToken(req);
        if (authToken is null) {
            return OneFuzzResult<UserAuthInfo>.Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find authorization token" });
        } else {
            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(authToken);
            var allowedTenants = await GetAllowedTenants();
            if (allowedTenants.IsOk) {
                if (allowedTenants.OkV is not null && allowedTenants.OkV.Contains(token.Issuer)) {

                    var userAuthInfo = new UserAuthInfo(new UserInfo(null, null, null), new List<string>());
                    var userInfo =
                        token.Payload.Claims.Aggregate(userAuthInfo, (acc, claim) => {
                            switch (claim.Type) {
                                case "oid":
                                    return acc with { UserInfo = acc.UserInfo with { ObjectId = Guid.Parse(claim.Value) } };
                                case "appId":
                                    return acc with { UserInfo = acc.UserInfo with { ApplicationId = Guid.Parse(claim.Value) } };
                                case "upn":
                                    return acc with { UserInfo = acc.UserInfo with { Upn = claim.Value } };
                                case "roles":
                                    acc.Roles.Add(claim.Value);
                                    return acc;
                                default:
                                    return acc;
                            }
                        });

                    return OneFuzzResult<UserAuthInfo>.Ok(userInfo);
                } else {
                    var tenantsStr = allowedTenants.OkV is null ? "null" : String.Join(';', allowedTenants.OkV!);
                    _log.Error($"issuer not from allowed tenant. issuer: {token.Issuer:Tag:Issuer} - tenants: {tenantsStr:Tag:Tenants}");
                    return OneFuzzResult<UserAuthInfo>.Error(ErrorCode.INVALID_REQUEST, new[] { "unauthorized AAD issuer" });
                }
            } else {
                _log.Error($"Failed to get allowed tenants due to {allowedTenants.ErrorV:Tag:Error}");
                return OneFuzzResult<UserAuthInfo>.Error(allowedTenants.ErrorV);
            }
        }
    }
}
