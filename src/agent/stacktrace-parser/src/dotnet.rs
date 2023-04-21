// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::Path;

use regex::Regex;

use crate::{CrashLogSummary, StackEntry};

// this has exactly 3 spaces at the start
const STACK_FRAME_PREFIX: &str = "   at ";

// Attempts to extract a .NET stack trace from a crash log.
pub(crate) fn parse_summary(text: &str) -> Option<CrashLogSummary> {
    let lines: Vec<&str> = text.lines().collect();

    // find first line starting with "  at "
    let (ix, _) = lines
        .iter()
        .enumerate()
        .find(|(_, l)| l.starts_with(STACK_FRAME_PREFIX))?;

    // search back to find line starting at column 0
    let (ix_first, _) = lines
        .iter()
        .enumerate()
        .take(ix)
        .rev()
        .find(|(_, l)| !l.is_empty() && !l.starts_with(' '))?;

    // collect up all exception messages:
    let exception_messages = lines
        .into_iter()
        .skip(ix_first)
        .take(ix - ix_first)
        .collect::<Vec<&str>>()
        .concat();

    Some(CrashLogSummary {
        summary: exception_messages,
        sanitizer: ".NET".to_string(),
        fault_type: "Unhandled exception".to_string(),
    })
}

lazy_static::lazy_static! {
    static ref STACK_FRAME_REGEX: Regex = Regex::new(r"^   at (?P<function_name>.*?)(?: in (?P<file_name>.*?):line (?P<line_number>\d+))?$").unwrap();
}

pub(crate) fn parse_dotnet_callstack(text: &str) -> Vec<StackEntry> {
    const END_OF_STACK_TRACE_MARKER: &str = "   --- End of inner exception stack trace ---";

    let mut result = vec![];
    for line in text.lines() {
        if line == END_OF_STACK_TRACE_MARKER {
            // this wasn’t the outer stack trace; dump progress so far
            result.clear();
        } else if let Some(parsed) = STACK_FRAME_REGEX.captures(line) {
            // rudimentary at the moment:
            let source_file_path = parsed.name("file_name").map(|m| m.as_str().to_string());
            let source_file_name = source_file_path.as_deref().and_then(|m| {
                Path::new(m)
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
            });

            let source_file_line = parsed
                .name("line_number")
                .and_then(|m| str::parse::<u64>(m.as_str()).ok());

            result.push(StackEntry {
                line: line.to_string(),
                function_name: parsed.name("function_name").map(|m| m.as_str().to_string()),
                source_file_path,
                source_file_name,
                source_file_line,
                ..Default::default()
            });
        }
    }

    result
}

#[cfg(test)]
mod test {
    use crate::StackEntry;
    use pretty_assertions::assert_eq;

    const SAMPLE_NO_LINENUMBERS: &str = r#"
INFO: libFuzzer ignores flags that start with '--'
INFO: Running with entropic power schedule (0xFF, 100).
INFO: Seed: 2166901369
INFO: Loaded 1 modules   (62 inline 8-bit counters): 62 [0x5638c2758000, 0x5638c275803e), 
INFO: Loaded 1 PC tables (62 PCs): 62 [0x5638c2758040,0x5638c2758420), 
INFO: 65536 Extra Counters
./libfuzzer-dotnet: Running 1 inputs 1 time(s) each.
Running: /workspaces/onefuzz/src/integration-tests/GoodBad/crash-64641bf3cd8aca3e3cc07ebe8a55436cf93e9ee3
System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at GoodBad.BinaryParser.ProcessInput(ReadOnlySpan`1 data)
   at GoodBad.Fuzzer.TestInput(ReadOnlySpan`1 data)
   at SharpFuzz.Fuzzer.LibFuzzer.Run(ReadOnlySpanAction action)
==25524== ERROR: libFuzzer: deadly signal
    #0 0x5638c2723b94 in __sanitizer_print_stack_trace (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x68b94) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #1 0x5638c26fa5a8 in fuzzer::PrintStackTrace() (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x3f5a8) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #2 0x5638c26e0023 in fuzzer::Fuzzer::CrashCallback() (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x25023) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #3 0x7f51e300251f  (/lib/x86_64-linux-gnu/libc.so.6+0x4251f) (BuildId: 69389d485a9793dbe873f0ea2c93e02efaa9aa3d)
    #4 0x5638c2724ae6 in LLVMFuzzerTestOneInput (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x69ae6) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #5 0x5638c26e15b3 in fuzzer::Fuzzer::ExecuteCallback(unsigned char const*, unsigned long) (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x265b3) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #6 0x5638c26cb32f in fuzzer::RunOneTest(fuzzer::Fuzzer*, char const*, unsigned long) (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x1032f) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #7 0x5638c26d1086 in fuzzer::FuzzerDriver(int*, char***, int (*)(unsigned char const*, unsigned long)) (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x16086) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #8 0x5638c26faea2 in main (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0x3fea2) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)
    #9 0x7f51e2fe9d8f  (/lib/x86_64-linux-gnu/libc.so.6+0x29d8f) (BuildId: 69389d485a9793dbe873f0ea2c93e02efaa9aa3d)
    #10 0x7f51e2fe9e3f in __libc_start_main (/lib/x86_64-linux-gnu/libc.so.6+0x29e3f) (BuildId: 69389d485a9793dbe873f0ea2c93e02efaa9aa3d)
    #11 0x5638c26c5bf4 in _start (/workspaces/onefuzz/src/integration-tests/GoodBad/libfuzzer-dotnet+0xabf4) (BuildId: d096e3fad0effc0b4b767afc99ef289ff780dc6e)

NOTE: libFuzzer has rudimentary signal handlers.
      Combine libFuzzer with AddressSanitizer or similar for better crash reports.
SUMMARY: libFuzzer: deadly signal"#;

    #[test]
    pub fn check_parse_sample_summary() {
        let result = super::parse_summary(SAMPLE_NO_LINENUMBERS).unwrap();
        assert_eq!(
            "System.IndexOutOfRangeException: Index was outside the bounds of the array.",
            result.summary
        );
    }

    #[test]
    pub fn check_parse_sample_stack() {
        let result = super::parse_dotnet_callstack(SAMPLE_NO_LINENUMBERS);
        assert_eq!(
            vec![
                StackEntry {
                    line: "   at GoodBad.BinaryParser.ProcessInput(ReadOnlySpan`1 data)"
                        .to_string(),
                    function_name: Some(
                        "GoodBad.BinaryParser.ProcessInput(ReadOnlySpan`1 data)".to_string()
                    ),
                    ..Default::default()
                },
                StackEntry {
                    line: "   at GoodBad.Fuzzer.TestInput(ReadOnlySpan`1 data)".to_string(),
                    function_name: Some(
                        "GoodBad.Fuzzer.TestInput(ReadOnlySpan`1 data)".to_string()
                    ),
                    ..Default::default()
                },
                StackEntry {
                    line: "   at SharpFuzz.Fuzzer.LibFuzzer.Run(ReadOnlySpanAction action)"
                        .to_string(),
                    function_name: Some(
                        "SharpFuzz.Fuzzer.LibFuzzer.Run(ReadOnlySpanAction action)".to_string()
                    ),
                    ..Default::default()
                }
            ],
            result
        );
    }

    const SAMPLE_NESTED_LINENUMBERS: &str = r#"
Unhandled exception. System.Exception: No fuzzing target specified
 ---> System.Exception: Missing `LIBFUZZER_DOTNET_TARGET` environment variables: LIBFUZZER_DOTNET_TARGET_ASSEMBLY, LIBFUZZER_DOTNET_TARGET_CLASS, LIBFUZZER_DOTNET_TARGET_METHOD
   at LibFuzzerDotnetLoader.LibFuzzerDotnetTarget.FromEnvironmentVars() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 190
   at LibFuzzerDotnetLoader.LibFuzzerDotnetTarget.FromEnvironment() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 166
   --- End of inner exception stack trace ---
   at LibFuzzerDotnetLoader.LibFuzzerDotnetTarget.FromEnvironment() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 171
   at LibFuzzerDotnetLoader.Program.TryMain() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 70
   at LibFuzzerDotnetLoader.Program.Main(String[] args) in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 57"#;

    #[test]
    pub fn check_parse_nested_sample_summary() {
        let result = super::parse_summary(SAMPLE_NESTED_LINENUMBERS).unwrap();
        assert_eq!(
            "Unhandled exception. System.Exception: No fuzzing target specified ---> System.Exception: Missing `LIBFUZZER_DOTNET_TARGET` environment variables: LIBFUZZER_DOTNET_TARGET_ASSEMBLY, LIBFUZZER_DOTNET_TARGET_CLASS, LIBFUZZER_DOTNET_TARGET_METHOD",
            result.summary
        );
    }

    #[test]
    pub fn check_parse_nested_sample_stack() {
        let result = super::parse_dotnet_callstack(SAMPLE_NESTED_LINENUMBERS);
        assert_eq!(
            vec![
                StackEntry {
                    line: "   at LibFuzzerDotnetLoader.LibFuzzerDotnetTarget.FromEnvironment() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 171"
                        .to_string(),
                    function_name: Some("LibFuzzerDotnetLoader.LibFuzzerDotnetTarget.FromEnvironment()".to_string()),
                    source_file_path: Some("/workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs".to_string()),
                    source_file_name: Some("Program.cs".to_string()),
                    source_file_line: Some(171),
                    ..Default::default()
                },
                StackEntry {
                    line: "   at LibFuzzerDotnetLoader.Program.TryMain() in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 70"
                        .to_string(),
                    function_name: Some("LibFuzzerDotnetLoader.Program.TryMain()".to_string()),
                    source_file_path: Some("/workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs".to_string()),
                    source_file_name: Some("Program.cs".to_string()),
                    source_file_line: Some(70),
                    ..Default::default()
                },
                StackEntry {
                    line: "   at LibFuzzerDotnetLoader.Program.Main(String[] args) in /workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs:line 57"
                        .to_string(),
                    function_name: Some("LibFuzzerDotnetLoader.Program.Main(String[] args)".to_string()),
                    source_file_path: Some("/workspaces/onefuzz/src/agent/LibFuzzerDotnetLoader/Program.cs".to_string()),
                    source_file_name: Some("Program.cs".to_string()),
                    source_file_line: Some(57),
                    ..Default::default()
                },
            ],
            result
        );
    }

    const DOUBLE_NESTED_SAMPLE: &str = r#"
System.Exception: This is the outermost exception
 ---> System.InvalidOperationException: This is the exception containing the innermost exception
 ---> System.ArgumentNullException: This is the innermost exception (Parameter 'something')
   at StackOverflow44227962StackTraces.ClassC.DoSomething(Object something) in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassC.cs:line 15
   at StackOverflow44227962StackTraces.ClassB.DoSomething() in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassB.cs:line 12
   --- End of inner exception stack trace ---
   at StackOverflow44227962StackTraces.ClassB.DoSomething() in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassB.cs:line 16
   at StackOverflow44227962StackTraces.ClassA.CallClassB() in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassA.cs:line 23
   at StackOverflow44227962StackTraces.ClassA.DoSomething() in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassA.cs:line 11
   --- End of inner exception stack trace ---
   at StackOverflow44227962StackTraces.ClassA.DoSomething() in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\ClassA.cs:line 15
   at StackOverflow44227962StackTraces.Program.Main(String[] args) in C:\Users\Simon\source\repos\StackOverflow44227962StackTraces\Program.cs:line 12
"#;

    #[test]
    pub fn check_parse_double_nested_sample_summary() {
        let result = super::parse_summary(DOUBLE_NESTED_SAMPLE).unwrap();
        assert_eq!(
            result.summary,
            "System.Exception: This is the outermost exception \
            ---> System.InvalidOperationException: This is the exception containing the innermost exception \
            ---> System.ArgumentNullException: This is the innermost exception (Parameter 'something')"
        );
    }
}
