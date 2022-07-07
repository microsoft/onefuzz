// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;

using TestOneSpan = SharpFuzz.ReadOnlySpanAction;
delegate void TestOneArray(byte[] data);

namespace LibFuzzerDotnetLoader {
    class EnvVar
    {
        // Fuzz targets can be specified by setting this environment variable as `<assembly>:<class>:<method>`.
        public const string TARGET = "LIBFUZZER_DOTNET_TARGET";

        // Fuzz targets can also be specified by setting each of these environment variables.
        public const string ASSEMBLY = "LIBFUZZER_DOTNET_TARGET_ASSEMBLY";
        public const string CLASS = "LIBFUZZER_DOTNET_TARGET_CLASS";
        public const string METHOD = "LIBFUZZER_DOTNET_TARGET_METHOD";
    }

    public class Program {
        public static void Main(string[] args) {
            var target = LibFuzzerDotnetTarget.FromEnvironment();

            var assem = Assembly.LoadFrom(target.AssemblyPath);

            var ty = assem.GetType(target.ClassName)??
                throw new Exception($"unable to resolve type: {target.ClassName}");

            var method = ty.GetMethod(target.MethodName)??
                throw new Exception($"unable to resolve method: {target.MethodName}");

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
        public string AssemblyPath { get; }
        public string ClassName { get; }
        public string MethodName { get; }

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
            var assemblyPath = Environment.GetEnvironmentVariable(EnvVar.ASSEMBLY)??
                throw new Exception($"`{EnvVar.ASSEMBLY}` not set");
            var className = Environment.GetEnvironmentVariable(EnvVar.CLASS)??
                throw new Exception($"`{EnvVar.CLASS}` not set");
            var methodName = Environment.GetEnvironmentVariable(EnvVar.METHOD)??
                throw new Exception($"`{EnvVar.METHOD}` not set");

            return new LibFuzzerDotnetTarget(assemblyPath, className, methodName);
        }


        static LibFuzzerDotnetTarget FromEnvironmentVarDelimited()
        {
            var target = Environment.GetEnvironmentVariable(EnvVar.TARGET)??
                throw new Exception($"`{EnvVar.TARGET}` not set." +
                                    "Expected format: \"<assembly-path>:<class>:<static-method>\"");

            var parts = target.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 3)
            {
                throw new Exception($"Value of `{EnvVar.TARGET}` is invalid");
            }

            return new LibFuzzerDotnetTarget(parts[0], parts[1], parts[2]);
        }
    }
}
