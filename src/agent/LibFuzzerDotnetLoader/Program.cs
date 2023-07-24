// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
namespace LibFuzzerDotnetLoader;

using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

using TestOneSpan = SharpFuzz.ReadOnlySpanAction;
delegate void TestOneArray(byte[] data);

class EnvVar
{
    // Fuzz targets can be specified by setting this environment variable as `<assembly>:<class>:<method>`.
    public const string TARGET = "LIBFUZZER_DOTNET_TARGET";

    // Fuzz targets can also be specified by setting each of these environment variables.
    public const string ASSEMBLY = "LIBFUZZER_DOTNET_TARGET_ASSEMBLY";
    public const string CLASS = "LIBFUZZER_DOTNET_TARGET_CLASS";
    public const string METHOD = "LIBFUZZER_DOTNET_TARGET_METHOD";
}

class Logging
{
    public static ILogger CreateLogger<T>()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("LibFuzzerDotnetLoader.Program", LogLevel.Debug)
                .AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                        o.TimestampFormat = "HH:mm:ss ";
                    }
                )
        );

        return loggerFactory.CreateLogger<T>();
    }
}

public class Program
{
    static ILogger logger;

    static Program()
    {
        logger = Logging.CreateLogger<Program>();
    }

    public static void Main(string[] args)
    {
        try
        {
            TryMain();
        }
        catch (Exception e)
        {
            logger.LogError($"{e.Message}");
            throw;
        }
    }

    static void TryMain()
    {
        logger.LogDebug("Checking environment for target specification");

        var target = LibFuzzerDotnetTarget.FromEnvironment();

        logger.LogDebug($"Attempting to load assembly from `{target.AssemblyPath}`");

        var loadContext = new FuzzerAssemblyLoadContext(target.AssemblyPath);
        var assem = loadContext.LoadFromAssemblyName(AssemblyName.GetAssemblyName(target.AssemblyPath));

        var ty = assem.GetType(target.ClassName) ??
            throw new Exception($"unable to resolve type: {target.ClassName}");

        var method = ty.GetMethod(target.MethodName) ??
            throw new Exception($"unable to resolve method: {target.MethodName}");

        if (TryTestOneSpan(method))
        {
            return;
        }

        logger.LogWarning($"Unable to bind method `{target.ClassName}.{target.MethodName}` to signature `void (ReadOnlySpan<byte>)`.");
        logger.LogWarning("Attempting to bind to signature `void (byte[])`.");
        logger.LogWarning("This will require an extra copy of the test input on each iteration.");
        logger.LogWarning("Modify your target method to accept `ReadOnlySpan<byte>` if your project supports it.");

        if (TryTestOneArray(method))
        {
            return;
        }

        throw new Exception($"unable to bind method to a known delegate type: {target.MethodName}");
    }

    // Returns `true` if delegate binding succeeded.
    static bool TryTestOne<T>(MethodInfo method, Func<T, SharpFuzz.ReadOnlySpanAction> createAction)
        where T : System.Delegate
    {
        T testOneInput;

        try
        {
            testOneInput = (T)Delegate.CreateDelegate(typeof(T), method);
            logger.LogDebug($"Bound method `{method}` to delegate `{typeof(T)}`");
        }
        catch
        {
            // We failed to bind to the target method.
            //
            // Return `false`, so the caller can try a binding with a legacy signature.
            return false;
        }

        var action = createAction(testOneInput);

        logger.LogInformation($"Running method `{method}`...");
        SharpFuzz.Fuzzer.LibFuzzer.Run(action);

        return true;
    }

    static bool TryTestOneSpan(MethodInfo method)
    {
        return TryTestOne<TestOneSpan>(method, t => t);
    }

    static bool TryTestOneArray(MethodInfo method)
    {
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

    static ILogger logger;

    static LibFuzzerDotnetTarget()
    {
        logger = Logging.CreateLogger<LibFuzzerDotnetTarget>();
    }

    public LibFuzzerDotnetTarget(string assemblyPath, string className, string methodName)
    {
        AssemblyPath = assemblyPath;
        ClassName = className;
        MethodName = methodName;
    }

    public static LibFuzzerDotnetTarget FromEnvironment()
    {
        try
        {
            logger.LogDebug($"Checking {EnvVar.TARGET} for `:`-delimited target specification.");
            return FromEnvironmentVarDelimited();
        }
        catch (Exception e)
        {
            logger.LogDebug($"Couldn't find target specification in `{EnvVar.TARGET}`: {e.Message}");
        }

        try
        {
            logger.LogDebug($"Checking {EnvVar.ASSEMBLY}, {EnvVar.CLASS}, and {EnvVar.METHOD} for target specification.");
            return FromEnvironmentVars();
        }
        catch (Exception e)
        {
            logger.LogDebug($"Couldn't find target specification in individual environment variables : {e.Message}");
            throw new Exception("No fuzzing target specified", e);
        }
    }

    static LibFuzzerDotnetTarget FromEnvironmentVars()
    {
        var assemblyPath = Environment.GetEnvironmentVariable(EnvVar.ASSEMBLY);
        var className = Environment.GetEnvironmentVariable(EnvVar.CLASS);
        var methodName = Environment.GetEnvironmentVariable(EnvVar.METHOD);

        var missing = new List<string>();

        if (assemblyPath is null) { missing.Add(EnvVar.ASSEMBLY); }
        if (className is null) { missing.Add(EnvVar.CLASS); }
        if (methodName is null) { missing.Add(EnvVar.METHOD); }

        if (assemblyPath is null || className is null || methodName is null)
        {
            var vars = String.Join(", ", missing);
            throw new Exception($"Missing `LIBFUZZER_DOTNET_TARGET` environment variables: {vars}");
        }

        return new LibFuzzerDotnetTarget(assemblyPath, className, methodName);
    }


    static LibFuzzerDotnetTarget FromEnvironmentVarDelimited()
    {
        var target = Environment.GetEnvironmentVariable(EnvVar.TARGET) ??
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

sealed class FuzzerAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public FuzzerAssemblyLoadContext(string path)
    {
        _resolver = new AssemblyDependencyResolver(path);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is not null)
        {
            return LoadFromAssemblyPath(path);
        }

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is not null)
        {
            return LoadUnmanagedDllFromPath(path);
        }

        return nint.Zero;
    }
}
