using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Auth;

public sealed class AuthorizationMiddleware : IFunctionsWorkerMiddleware {
    private readonly IEndpointAuthorization _auth;
    private readonly ILogger _log;

    public AuthorizationMiddleware(IEndpointAuthorization auth, ILogger<AuthorizationMiddleware> log) {
        _auth = auth;
        _log = log;
    }

    public async Async.Task Invoke(FunctionContext context, FunctionExecutionDelegate next) {
        var attribute = GetAuthorizeAttribute(context);
        if (attribute is not null) {
            var req = await context.GetHttpRequestDataAsync() ?? throw new NotSupportedException("no HTTP request data found");
            var user = context.TryGetUserAuthInfo();
            if (user is null) {
                await Reject(req, context, "no authentication");
                return;
            }

            var (isAgent, _) = await _auth.IsAgent(user);
            if (isAgent) {
                if (attribute.Allow != Allow.Agent) {
                    await Reject(req, context, "endpoint not allowed for agents");
                    return;
                }
            } else {
                if (attribute.Allow == Allow.Agent) {
                    await Reject(req, context, "endpoint not allowed for users");
                    return;
                }

                Debug.Assert(attribute.Allow is Allow.User or Allow.Admin);

                // check access control first
                var access = await _auth.CheckAccess(req);
                if (!access.IsOk) {
                    await Reject(req, context, "access control rejected request");
                    return;
                }

                // check admin next
                if (attribute.Allow == Allow.Admin) {
                    var adminAccess = await _auth.CheckRequireAdmins(user);
                    if (!adminAccess.IsOk) {
                        await Reject(req, context, "must be admin to use this endpoint");
                        return;
                    }
                }
            }
        }

        await next(context);
    }

    private static async Async.ValueTask Reject(HttpRequestData request, FunctionContext context, string reason) {
        var response = HttpResponseData.CreateResponse(request);
        var status = HttpStatusCode.Unauthorized;
        await response.WriteAsJsonAsync(
            new ProblemDetails(
                status,
                Error.Create(ErrorCode.UNAUTHORIZED, reason)),
            "application/problem+json",
            status);

        context.GetInvocationResult().Value = response;
    }

    // use ImmutableDictionary to prevent needing to lock and without the overhead
    // of ConcurrentDictionary
    private static ImmutableDictionary<string, AuthorizeAttribute?> _authorizeCache =
        ImmutableDictionary.Create<string, AuthorizeAttribute?>();

    private static AuthorizeAttribute? GetAuthorizeAttribute(FunctionContext context) {
        // fully-qualified name of the method
        var entryPoint = context.FunctionDefinition.EntryPoint;
        if (_authorizeCache.TryGetValue(entryPoint, out var cached)) {
            return cached;
        }

        var lastDot = entryPoint.LastIndexOf('.');
        var (typeName, methodName) = (entryPoint[..lastDot], entryPoint[(lastDot + 1)..]);
        var assemblyPath = context.FunctionDefinition.PathToAssembly;
        var assembly = Assembly.LoadFrom(assemblyPath); // should already be loaded
        var type = assembly.GetType(typeName)!;
        var method = type.GetMethod(methodName)!;
        var result =
            method.GetCustomAttribute<AuthorizeAttribute>()
            ?? type.GetCustomAttribute<AuthorizeAttribute>();

        _authorizeCache = _authorizeCache.SetItem(entryPoint, result);
        return result;
    }
}
