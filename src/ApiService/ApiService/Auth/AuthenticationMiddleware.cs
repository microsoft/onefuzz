using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service.Auth;

public sealed class AuthenticationMiddleware : IFunctionsWorkerMiddleware {
    private readonly IConfigOperations _config;
    private readonly ILogger _log;

    public AuthenticationMiddleware(IConfigOperations config, ILogger<AuthenticationMiddleware> log) {
        _config = config;
        _log = log;
    }

    public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next) {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData is not null) {
            var authToken = GetAuthToken(requestData);
            if (authToken is not null) {
                // note that no validation of the token is performed here
                // this is done globally by Azure Functions; see the configuration in
                // 'function.bicep'
                var token = new JwtSecurityToken(authToken);
                var allowedTenants = await AllowedTenants();
                if (!allowedTenants.Contains(token.Issuer)) {
                    await BadIssuer(requestData, context, token, allowedTenants);
                    return;
                }

                context.SetUserAuthInfo(UserInfoFromAuthToken(token));
            }
        }

        await next(context);
    }

    private static UserAuthInfo UserInfoFromAuthToken(JwtSecurityToken token)
        => token.Payload.Claims.Aggregate(
            seed: new UserAuthInfo(new UserInfo(null, null, null), new List<string>()),
            (acc, claim) => {
                switch (claim.Type) {
                    case "oid":
                        return acc with { UserInfo = acc.UserInfo with { ObjectId = Guid.Parse(claim.Value) } };
                    case "appid":
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

    private async Async.ValueTask BadIssuer(
        HttpRequestData request,
        FunctionContext context,
        JwtSecurityToken token,
        IEnumerable<string> allowedTenants) {

        var tenantsStr = string.Join("; ", allowedTenants);
        _log.LogError($"issuer not from allowed tenant. issuer: {token.Issuer:Tag:Issuer} - tenants: {tenantsStr:Tag:Tenants}");

        var response = HttpResponseData.CreateResponse(request);
        var status = HttpStatusCode.BadRequest;
        await response.WriteAsJsonAsync(
            new ProblemDetails(
                status,
                new Error(
                    ErrorCode.INVALID_REQUEST,
                    new List<string> {
                        "unauthorized AAD issuer. If multi-tenant auth is failing, make sure to include all tenant_ids in the `allowed_aad_tenants` list in the instance_config. To see the current instance_config, run `onefuzz instance_config get`. "
                    }
                )),
            "application/problem+json",
            status);

        context.GetInvocationResult().Value = response;
    }

    private async Async.Task<IEnumerable<string>> AllowedTenants() {
        var config = await _config.Fetch();
        return config.AllowedAadTenants.Select(t => $"https://sts.windows.net/{t}/");
    }

    private static string? GetAuthToken(HttpRequestData requestData)
        => GetBearerToken(requestData) ?? GetAadIdToken(requestData);

    private static string? GetAadIdToken(HttpRequestData requestData) {
        if (!requestData.Headers.TryGetValues("x-ms-token-aad-id-token", out var values)) {
            return null;
        }

        return values.First();
    }

    private static string? GetBearerToken(HttpRequestData requestData) {
        if (!requestData.Headers.TryGetValues("Authorization", out var values)
            || !AuthenticationHeaderValue.TryParse(values.First(), out var headerValue)
            || !string.Equals(headerValue.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return headerValue.Parameter;
    }
}
