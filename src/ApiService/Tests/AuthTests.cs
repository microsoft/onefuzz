using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Castle.Core.Internal;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.Auth;
using Xunit;

namespace Tests;

public class AuthTests {

    public static IEnumerable<object[]> AllFunctionEntryPoints() {
        var asm = typeof(AuthorizeAttribute).Assembly;
        foreach (var type in asm.GetTypes()) {
            if (type.Namespace == "ApiService.TestHooks"
                || type.Name == "TestHooks") {
                // skip test hooks
                continue;
            }

            foreach (var method in type.GetMethods()) {
                if (method.GetCustomAttribute<FunctionAttribute>() is not null) {
                    // it's a function entrypoint
                    yield return new object[] { type, method };
                }
            }
        }
    }


    [Theory]
    [MemberData(nameof(AllFunctionEntryPoints))]
    public void AllFunctionsHaveAuthAttributes(Type type, MethodInfo methodInfo) {
        var trigger = methodInfo.GetParameters().First().GetCustomAttribute<HttpTriggerAttribute>();
        if (trigger is null) {
            return; // not an HTTP function
        }

        // built-in auth level should be anonymous - we are implementing our own authorization
        Assert.Equal(AuthorizationLevel.Anonymous, trigger.AuthLevel);

        if (type.Name == "Config" && methodInfo.Name == "Run") {
            // this method alone is allowed to be anonymous
            Assert.Null(methodInfo.GetAttribute<AuthorizeAttribute>());
            return;
        }

        // authorize attribute can be on class or method
        var authAttribute = methodInfo.GetAttribute<AuthorizeAttribute>()
            ?? type.GetAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);

        // naming convention: check that Agent* functions have Allow.Agent, and none other
        var functionAttribute = methodInfo.GetCustomAttribute<FunctionAttribute>()!;
        if (functionAttribute.Name.StartsWith("Agent")) {
            Assert.Equal(Allow.Agent, authAttribute.Allow);
        } else {
            Assert.NotEqual(Allow.Agent, authAttribute.Allow);
        }

        // naming convention: all *_Admin functions should be ALlow.Admin
        // (some that aren't _Admin also require it)
        if (functionAttribute.Name.EndsWith("_Admin")) {
            Assert.Equal(Allow.Admin, authAttribute.Allow);
        }

        // make sure other methods that _aren't_ function entry points don't have it,
        // because it won't do anything there, and having it present would be misleading
        foreach (var otherMethod in type.GetMethods()) {
            if (otherMethod.GetCustomAttribute<FunctionAttribute>() is null) {
                Assert.True(
                    otherMethod.GetCustomAttribute<AuthorizeAttribute>() is null,
                    "non-[Function] methods must not have [Authorize]");
            }
        }
    }
}
