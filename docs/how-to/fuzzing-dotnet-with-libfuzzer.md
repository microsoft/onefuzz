# Fuzzing .Net using libfuzzer-dotnet

Fuzzing .Net using libFuzzer makes use of three Open Source capabilities.

1. [SharpFuzz](https://github.com/Metalnem/sharpfuzz), which injects coverage tracking into .Net assembly
2. [libFuzzer](https://www.llvm.org/docs/LibFuzzer.html), which performs the fuzzing
3. [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet), which bridges the SharpFuzz instrumentation into libFuzzer

When using libFuzzer in C, developers provide a function
`LLVMFuzzerTestOneInput` which takes a pointer to a read-only buffer of bytes,
and the length of said buffer.  ([Tutorial using libFuzzer in
C](https://github.com/google/fuzzing/blob/master/tutorial/libFuzzerTutorial.md))

With libfuzzer-dotnet, developers provide an application that within `Main` calls the method `Fuzzer.LibFuzzer.Run`, with a callback that passes a read only byte-stream their function of interest.

> NOTE: libfuzzer-dotnet only works on Linux at this time.

TL/DR: check out our [libfuzzer-dotnet example](../../src/integration-tests/libfuzzer-dotnet/)

## Supported versions
OneFuzz supports net45 framework or any version that support least
netstandard1.6.  Refer to [.Net
Standard](https://dotnet.microsoft.com/platform/dotnet-standard) check if your
framework version is supported.

## Issues using libfuzzer-dotnet in OneFuzz
* The `coverage` task does not support the coverage features used by libfuzzer-dotnet.
* The `libfuzzer_crash_report` does not support extracting unique output during analysis, making the crash de-duplication and reporting ineffective. (Work item: [#538]https://github.com/microsoft/onefuzz/issues/538))

As such, a libfuzzer-dotnet template is available, which only uses the `libfuzzer_fuzz` tasks.  As these issues are resolve, the template will be updated to include the additional tasks.

## Example

Let's fuzz the `Func` function of our example library named [problems](../../src/integration-tests/libfuzzer-dotnet/problems/).

1. Make sure sharpfuzz and a recent version of clang are installed.  We'll need these later.

  ```
  dotnet tool install --global SharpFuzz.CommandLine
  sudo apt-get install -y clang
  ```

2. We need to build an application that uses `Fuzzer.LibFuzzer.Run` that calls our function `Func`.  For this example, let's call this [wrapper](../../src/integration-tests/libfuzzer-dotnet/wrapper/)

  The [wrapper/wrapper.csproj](../../src/integration-tests/libfuzzer-dotnet/wrapper/wrapper.csproj) project file uses SharpFuzz 1.6.1 and refers to our [problems](../../src/integration-tests/libfuzzer-dotnet/problems/) library locally.
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
       <ProjectReference Include="..\problems\problems.csproj" />
    </ItemGroup>
    <ItemGroup>
      <PackageReference Include="SharpFuzz" Version="1.6.1" />
    </ItemGroup>
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>netcoreapp3.1</TargetFramework>
    </PropertyGroup>
  </Project>
  ```

  For our example [problems](../../src/integration-tests/libfuzzer-dotnet/problems/) library, our callback for `Fuzzer.LibFuzzer.Run` is straight forwards.  `Func` already takes a `ReadOnlySpan<byte>`.  If your functions takes strings, this would be the place to convert the span of bytes to strings.
  [wrapper/program.cs](../../src/integration-tests/libfuzzer-dotnet/wrapper/program.cs)
  ```C#
  using SharpFuzz;
  namespace Wrapper {
    public class Program {
      public static void Main(string[] args) {
        Fuzzer.LibFuzzer.Run(stream => { Problems.Problems.Func(stream); });
      }
    }
  }
  ```

3. Build our [wrapper](../../src/integration-tests/libfuzzer-dotnet/wrapper/)
  ```
  dotnet publish ./wrapper/wrapper.csproj -c release -r linux-x64 -o my-fuzzer
  ```
  > NOTE: Specifying the runtime `linux-x64` is important such that we make a self-contained deployment.

4. Then we need to ensure our [problems](../../src/integration-tests/libfuzzer-dotnet/problems/) library is instrumented:
  ```
  sharpfuzz ./my-fuzzer/problems.dll
  ```

5. The last thing we need to build before we can start fuzzing is the [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) harness.
  ```
  curl -o libfuzzer-dotnet.cc https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/master/libfuzzer-dotnet.cc
  clang -fsanitize=fuzzer libfuzzer-dotnet.cc -o my-fuzzer/libfuzzer-dotnet
  ```

6. We should provide some sample inputs for our fuzzing. For this example, a basic file will do. However, this should include reasonable known-good inputs for your function. If you're fuzzing PNGs, use a selection of valid PNGs.
  ```
  mkdir -p inputs
  echo hi > inputs/hi.txt
  ```

7. Now we can fuzz!
  Execute `libfuzzer-dotnet` with our `wrapper` program and our `inputs` directory:
  ```
  ./my-fuzzer/libfuzzer-dotnet --target_path=./my-fuzzer/wrapper ./inputs/
  ```

  In a few seconds, you'll see output that looks something like this:
  ```
  INFO: libFuzzer ignores flags that start with '--'
  INFO: Seed: 2909502334
  INFO: Loaded 1 modules   (58 inline 8-bit counters): 58 [0x4f9090, 0x4f90ca),
  INFO: Loaded 1 PC tables (58 PCs): 58 [0x4bfae8,0x4bfe88),
  INFO: 65536 Extra Counters
  INFO: -max_len is not provided; libFuzzer will not generate inputs larger than 4096 bytes
  INFO: A corpus is not provided, starting from an empty corpus
  #2      INITED cov: 8 ft: 10 corp: 1/1b exec/s: 0 rss: 24Mb
  #3      NEW    cov: 8 ft: 14 corp: 2/5b lim: 4 exec/s: 0 rss: 24Mb L: 4/4 MS: 1 CrossOver-
  #36     NEW    cov: 8 ft: 16 corp: 3/9b lim: 4 exec/s: 0 rss: 24Mb L: 4/4 MS: 3 EraseBytes-CopyPart-CrossOver-
  #337    NEW    cov: 8 ft: 18 corp: 4/13b lim: 6 exec/s: 0 rss: 24Mb L: 4/4 MS: 1 CMP- DE: "\x01\x00"-
  System.Exception: this is bad
     at Problems.Problems.Func(ReadOnlySpan`1 data)
     at Wrapper.Program.<>c.<Main>b__0_0(ReadOnlySpan`1 stream) in /home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/wrapper/program.cs:line 5
     at SharpFuzz.Fuzzer.LibFuzzer.Run(ReadOnlySpanAction action)
  ==7346== ERROR: libFuzzer: deadly signal
      #0 0x4adf50 in __sanitizer_print_stack_trace (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x4adf50)
      #1 0x45a258 in fuzzer::PrintStackTrace() (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x45a258)
      #2 0x43f3a3 in fuzzer::Fuzzer::CrashCallback() (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x43f3a3)
      #3 0x7fd6c323f3bf  (/lib/x86_64-linux-gnu/libpthread.so.0+0x153bf)
      #4 0x4aef35 in LLVMFuzzerTestOneInput (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x4aef35)
      #5 0x440a61 in fuzzer::Fuzzer::ExecuteCallback(unsigned char const*, unsigned long) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x440a61)
      #6 0x4401a5 in fuzzer::Fuzzer::RunOne(unsigned char const*, unsigned long, bool, fuzzer::InputInfo*, bool*) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x4401a5)
      #7 0x442447 in fuzzer::Fuzzer::MutateAndTestOne() (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x442447)
      #8 0x443145 in fuzzer::Fuzzer::Loop(std::__Fuzzer::vector<fuzzer::SizedFile, fuzzer::fuzzer_allocator<fuzzer::SizedFile> >&) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x443145)
      #9 0x431afe in fuzzer::FuzzerDriver(int*, char***, int (*)(unsigned char const*, unsigned long)) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x431afe)
      #10 0x45a942 in main (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x45a942)
      #11 0x7fd6c2ee20b2 in __libc_start_main /build/glibc-eX1tMB/glibc-2.31/csu/../csu/libc-start.c:308:16
      #12 0x40689d in _start (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x40689d)

  NOTE: libFuzzer has rudimentary signal handlers.
        Combine libFuzzer with AddressSanitizer or similar for better crash reports.
  SUMMARY: libFuzzer: deadly signal
  MS: 4 ChangeBit-CopyPart-ShuffleBytes-PersAutoDict- DE: "\x01\x00"-; base unit: ae8444de02705346dae4f4c67d0c710b833c14e1
  0x0,0x1,0x0,0x0,0xe,0x0,
  \x00\x01\x00\x00\x0e\x00
  artifact_prefix='./'; Test unit written to ./crash-ad81c382bc24cb4edb13f5ab12ce1ee454600a69
  Base64: AAEAAA4A
  ```

  As shown in the output, our fuzzing run generated the file `crash-ad81c382bc24cb4edb13f5ab12ce1ee454600a69`.  If we provide this file on the command line, we can reproduce the identified crash:
  ```
  $ ./my-fuzzer/libfuzzer-dotnet --target_path=./my-fuzzer/wrapper ./crash-ad81c382bc24cb4edb13f5ab12ce1ee454600a69
  INFO: libFuzzer ignores flags that start with '--'
  INFO: Seed: 3044788143
  INFO: Loaded 1 modules   (58 inline 8-bit counters): 58 [0x4f9090, 0x4f90ca),
  INFO: Loaded 1 PC tables (58 PCs): 58 [0x4bfae8,0x4bfe88),
  INFO: 65536 Extra Counters
  ./my-fuzzer/libfuzzer-dotnet: Running 1 inputs 1 time(s) each.
  Running: ./crash-ad81c382bc24cb4edb13f5ab12ce1ee454600a69
  System.Exception: this is bad
     at Problems.Problems.Func(ReadOnlySpan`1 data)
     at Wrapper.Program.<>c.<Main>b__0_0(ReadOnlySpan`1 stream) in /home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/wrapper/program.cs:line 5
     at SharpFuzz.Fuzzer.LibFuzzer.Run(ReadOnlySpanAction action)
  ==7882== ERROR: libFuzzer: deadly signal
      #0 0x4adf50 in __sanitizer_print_stack_trace (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x4adf50)
      #1 0x45a258 in fuzzer::PrintStackTrace() (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x45a258)
      #2 0x43f3a3 in fuzzer::Fuzzer::CrashCallback() (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x43f3a3)
      #3 0x7f1681d223bf  (/lib/x86_64-linux-gnu/libpthread.so.0+0x153bf)
      #4 0x4aef35 in LLVMFuzzerTestOneInput (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x4aef35)
      #5 0x440a61 in fuzzer::Fuzzer::ExecuteCallback(unsigned char const*, unsigned long) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x440a61)
      #6 0x42c1d2 in fuzzer::RunOneTest(fuzzer::Fuzzer*, char const*, unsigned long) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x42c1d2)
      #7 0x431c86 in fuzzer::FuzzerDriver(int*, char***, int (*)(unsigned char const*, unsigned long)) (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x431c86)
      #8 0x45a942 in main (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x45a942)
      #9 0x7f16819c50b2 in __libc_start_main /build/glibc-eX1tMB/glibc-2.31/csu/../csu/libc-start.c:308:16
      #10 0x40689d in _start (/home/bcaswell/projects/onefuzz/onefuzz/src/integration-tests/libfuzzer-dotnet/my-fuzzer/libfuzzer-dotnet+0x40689d)

  NOTE: libFuzzer has rudimentary signal handlers.
        Combine libFuzzer with AddressSanitizer or similar for better crash reports.
  SUMMARY: libFuzzer: deadly signal
  ```
  > NOTE: The stack shown here is from `libfuzzer-dotnet`, which isn't useful in figuring out the bug in `Problems`. However, fuzzing found a reproducable crash in our `Problems` library. We can use this crashing input file using traditional debug tooling to figure out the underlying problem.


## Launching our example in OneFuzz

These commands launches the a libfuzzer-dotnet focused fuzzing task in OneFuzz.  Note, we've added the arguments `--wait_for_running --wait_for_files inputs` such that we can monitor our job until we've seen at least one new input found via fuzzing.
```bash
TARGET_PROJECT=Problems
TARGET_NAME=Func
TARGET_BUILD=1
FUZZ_POOL=linux
onefuzz template libfuzzer dotnet ${TARGET_PROJECT} ${TARGET_NAME} ${TARGET_BUILD} ${FUZZ_POOL} ./my-fuzzer/ wrapper --wait_for_running --wait_for_files inputs
```

When we run this, we'll see output similar to:
```
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 62d666d3-3373-4094-adbe-e705d722d698
INFO:onefuzz:using container: oft-setup-b8c2890353235ab497b14913fe2ee204
INFO:onefuzz:using container: oft-inputs-68e4cb24b37855f0b0abab9b533d5fc1
INFO:onefuzz:using container: oft-crashes-68e4cb24b37855f0b0abab9b533d5fc1
INFO:onefuzz:uploading setup dir `./my-fuzzer/`
INFO:onefuzz:done creating tasks
\ waiting on: libfuzzer_fuzz:init
| waiting on: libfuzzer_fuzz:scheduled
- waiting on: libfuzzer_fuzz:setting_up
INFO:onefuzz:tasks started
\ waiting for new files: oft-inputs-68e4cb24b37855f0b0abab9b533d5fc1
INFO:onefuzz:new files found
{
    "config": {
        "build": "1",
        "duration": 24,
        "name": "Func",
        "project": "Problems"
    },
    "end_time": "2021-02-12T01:52:45+00:00",
    "job_id": "62d666d3-3373-4094-adbe-e705d722d698",
    "state": "enabled",
    "task_info": [
        {
            "state": "running",
            "task_id": "e951e3ee-5097-46be-832d-08fff05507e4",
            "type": "libfuzzer_fuzz"
        }
    ],
    "user_info": {
        "application_id": "5e400594-28e9-4e26-97d8-8ea5bc2ff306",
        "object_id": "33041cd0-ad34-4168-a98d-c8e6b4b666cb",
        "upn": "example@contoso.com"
    }
}
```
