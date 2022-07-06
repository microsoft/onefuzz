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
            var target = LibFuzzerDotnetTarget.FromEnvironment();

            var assem = Assembly.LoadFrom(target.AssemblyPath);

            var ty = assem.GetType(target.ClassName);

            if (ty == null) {
                throw new Exception($"unable to resolve type: {target.ClassName}");
            }

            var method = ty.GetMethod(target.MethodName);

            if (method == null) {
                throw new Exception($"unable to resolve method: {target.MethodName}");
            }

            if (TryTestOneSpan(method)) {
                return;
            }

            if (TryTestOneArray(method)) {
                return;
            }

            throw new Exception($"unable to bind method to a known delegate type: {target.MethodName}");
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

    class LibFuzzerDotnetTarget
    {
        public string AssemblyPath { get; private set; }
        public string ClassName { get; private set; }
        public string MethodName { get; private set; }

        public LibFuzzerDotnetTarget(string assemblyPath, string className, string methodName)
        {
            AssemblyPath = assemblyPath;
            ClassName = className;
            MethodName = methodName;
        }

        public static LibFuzzerDotnetTarget FromEnvironment()
        {
            try {
                return FromEnvironmentVarDelimited();
            }
            catch
            {}

            try {
                return FromEnvironmentVars();
            }
            catch
            {}

            throw new Exception("No fuzzing target specified by environment variables");
        }

        static LibFuzzerDotnetTarget FromEnvironmentVars()
        {
            var assemblyPath = Environment.GetEnvironmentVariable("LIBFUZZER_DOTNET_TARGET_ASSEMBLY");

            if (assemblyPath is null)
            {
                throw new Exception("`LIBFUZZER_DOTNET_TARGET_ASSEMBLY` not set");
            }

            var className = Environment.GetEnvironmentVariable("LIBFUZZER_DOTNET_TARGET_CLASS");

            if (className is null)
            {
                throw new Exception("`LIBFUZZER_DOTNET_TARGET_CLASS` not set");
            }

            var methodName = Environment.GetEnvironmentVariable("LIBFUZZER_DOTNET_TARGET_METHOD");

            if (methodName is null)
            {
                throw new Exception("`LIBFUZZER_DOTNET_TARGET_METHOD` not set");
            }

            return new LibFuzzerDotnetTarget(assemblyPath, className, methodName);
        }


        static LibFuzzerDotnetTarget FromEnvironmentVarDelimited()
        {
            string? assemblyPath = null;
            string? className = null;
            string? methodName = null;

            var target = Environment.GetEnvironmentVariable("LIBFUZZER_DOTNET_TARGET");

            if (target is null)
            {
                throw new Exception("`LIBFUZZER_DOTNET_TARGET` not set. " +
                                    "Expected format: \"<assembly-path>:<class>:<static-method>\"");
            }

            var parts = target.Split(':');

            try
            {
                assemblyPath = parts[0];
            }
            catch {
                throw new Exception("Invalid `LIBFUZZER_DOTNET_TARGET` (missing assembly path)");
            }

            try
            {
                className = parts[1];
            }
            catch
            {
                throw new Exception("Invalid `LIBFUZZER_DOTNET_TARGET` (missing class name)");
            }

            try
            {
                methodName = parts[2];
            }
            catch
            {
                throw new Exception("Invalid `LIBFUZZER_DOTNET_TARGET` (missing method name)");
            }

            return new LibFuzzerDotnetTarget(assemblyPath, className, methodName);
        }
    }
}
