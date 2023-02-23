# LibFuzzerDotnetLoader

## About

`LibFuzzerDotnetLoader` is a dynamic, reusable SharpFuzz harness for use with
[SharpFuzz](https://github.com/Metalnem/sharpfuzz) with [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet).
It removes the need for your project to link against SharpFuzz and invoke `SharpFuzz.LibFuzzer.LibFuzzerDotnetLoader`.

## Usage

Suppose your project ships a managed library, `Fabrikam.dll`,
and we want to fuzz the `AppModel.Deserialize()` method below:

```csharp
namespace Fabrikam {
    class AppModel
    {
        // ...

        public static AppModel Deserialize(ReadOnlySpan<byte> data)
        {
            // ..
        }
    }
}
```

We need to export a static _test method_ with the signature `static void (ReadOnlySpan<byte>)`.
If your project does not support `Span` types,
you may instead export a method with the signature `static void (byte[])`,
with a performance cost of one extra copy of the input for each fuzzing iteration.

Our project may then look like:

```csharp
namespace Fabrikam {
    class AppModel
    {
        // ...

        public static AppModel Deserialize(ReadOnlySpan<byte> data)
        {
            // ..
        }
    }

    class AppModelFuzzer
    {
        // The test method, invoked repeatedly when fuzzing.
        //
        // How we name this doesn't matter: we only require a compatible signature.
        static void TestInput(ReadOnlySpan<byte> data)
        {
            // In this case, we just ignore successful results. Sometimes it may make
            // sense to make assertions about the result, test serialization/deserialization
            // round-tripping, etc.
            //
            // Any uncaught exception will be treated as a fuzzing test case failure.
            AppModel.Deserialize(data);
        }
    }
}
```

Assuming you have compiled `libfuzzer-dotnet.exe` and `LibFuzzerDotnetLoader` for your platform (see below),
you can now fuzz using the following steps:

```pwsh
# Create a corpus directory to store fuzzer-generated inputs that uncover new blocks of code.
mkdir corpus

# Instrument the DLL in-place to provide coverage feedback to guide fuzzing.
# This makes fuzzing much more effective.
#
# This only needs to be done (exactly) once for each DLL under test.
sharpfuzz Fabrikam.dll

# Specify the target method as an environment variable.
$env:LIBFUZZER_DOTNET_TARGET_ASSEMBLY = "./Fabrikam.dll"  # Assume in working directory.
$env:LIBFUZZER_DOTNET_TARGET_CLASS = "Fabrikam.AppModelFuzzer"
$env:LIBFUZZER_DOTNET_TARGET_METHOD = "TestOneInput"

# Fuzz!
./libfuzzer-dotnet.exe --target_path="./LibFuzzerDotnetLoader.exe" corpus
```

The fuzzer will then run until it finds a "crash" (uncaught exception), saving "interesting" generated
inputs (i.e. which led to new program coverage) as it goes.

Alternately, you can specify the fuzz target using the following `:`-delimited shorthand:

```pwsh
$env:LIBFUZZER_DOTNET_TARGET = "./Fabrikam.dll:Fabrikam.AppModelFuzzer:TestOneInput"

./libfuzzer-dotnet.exe --target_path="./LibFuzzerDotnetLoader.exe" corpus
```

## Building

### Build `libfuzzer-dotnet`

First, make sure you have a `libfuzzer-dotnet` binary for your platform.
This must be based on commit `55d84f8` or later.

#### Linux
```
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
```

#### Windows
```
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet-windows.cc -o libfuzzer-dotnet.exe
```

### Build `LibFuzzerDotnetLoader`

Next, using .NET 6, you need to [publish](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) a self-contained
build of the loader.

Note: this depends on SharpFuzz 2.0.0 or greater.

#### Linux
```
dotnet publish src/SharpFuzz.CommandLine -f net7.0 -c Release --sc -r linux-x64
```

#### Windows 10
```
dotnet publish src\SharpFuzz.CommandLine -f net7.0 -c Release --sc -r win10-x64
```

In the end, you should have two binaries for your platform: `libfuzzer-dotnet`(`.exe`) and `LibFuzzerDotnetLoader`(.`exe`).
Together with the `sharpfuzz` CLI tool for instrumentation assemblies, you are now ready to fuzz.
