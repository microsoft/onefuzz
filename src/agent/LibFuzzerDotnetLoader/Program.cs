// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;

using TestOneSpan = SharpFuzz.ReadOnlySpanAction;
delegate void TestOneArray(byte[] data);

namespace LibFuzzerDotnetLoader {
    public class Program {
        public static void Main(string[] args) {
            var target = Environment.GetEnvironmentVariable("LIBFUZZER_DOTNET_TARGET");

            if (target == null) {
                throw new Exception("`LIBFUZZER_DOTNET_TARGET` not set. " +
                                    "Expected format: \"<assembly-path>:<class>:<static-method>\"");
            }

            var parts = target.Split(':');

            var assemPath = parts[0];
            var typeName = parts[1];
            var methodName = parts[2];

            var assem = Assembly.LoadFrom(assemPath);

            var ty = assem.GetType(typeName);

            if (ty == null) {
                throw new Exception($"unable to resolve type: {typeName}");
            }

            var method = ty.GetMethod(methodName);

            if (method == null) {
                throw new Exception($"unable to resolve method: {methodName}");
            }

            if (TryTestOneSpan(method)) {
                return;
            }

            if (TryTestOneArray(method)) {
                return;
            }

            throw new Exception($"unable to bind method to a known delegate type: {methodName}");
        }

        // Returns `true` if delegate binding succeeded.
        static bool TryTestOne<T>(MethodInfo method, Func<T, SharpFuzz.ReadOnlySpanAction> createAction)
            where T : System.Delegate
        {
            T testOneInput;

            try {
                testOneInput = (T) Delegate.CreateDelegate(typeof(T), method);
            } catch {
                // We failed to bind to the target method.
                //
                // Return `false`, so the caller can try a binding with a legacy signature.
                return false;
            }

            var action = createAction(testOneInput);
            SharpFuzz.Fuzzer.LibFuzzer.Run(action);

            return true;
        }

        static bool TryTestOneSpan(MethodInfo method) {
            return TryTestOne<TestOneSpan>(method, t => t);
        }

        static bool TryTestOneArray(MethodInfo method) {
            // Copy span data into a `byte[]` to support assemblies that target pre-`Span`
            // frameworks.
            return TryTestOne<TestOneArray>(method, t => span => t(span.ToArray()));
        }
    }
}
