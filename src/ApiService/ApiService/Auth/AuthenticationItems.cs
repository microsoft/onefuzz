using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Auth;

public static class AuthenticationItems {
    private const string Key = "ONEFUZZ_USER_INFO";

    public static void SetUserAuthInfo(this FunctionContext context, UserAuthInfo info)
        => context.Items[Key] = info;

    public static UserAuthInfo GetUserAuthInfo(this FunctionContext context)
        => (UserAuthInfo)context.Items[Key];

    public static UserAuthInfo? TryGetUserAuthInfo(this FunctionContext context)
        => context.Items.TryGetValue(Key, out var result) ? (UserAuthInfo)result : null;
}
