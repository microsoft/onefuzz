// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use libclusterfuzz::get_stack_filter;
use sha2::{Digest, Sha256};

mod asan;

#[derive(Clone, Debug, Default)]
pub struct CrashLog {
    pub text: String,
    pub sanitizer: String,
    pub summary: String,
    pub fault_type: String,
    pub call_stack: Vec<String>,
    pub full_stack_details: Vec<StackEntry>,
    pub full_stack_names: Vec<String>,
    pub minimal_stack_details: Vec<StackEntry>,
    pub minimal_stack: Vec<String>,
    pub scariness_score: Option<u32>,
    pub scariness_description: Option<String>,
}

impl CrashLog {
    pub fn parse(text: String) -> Result<Self> {
        let (summary, sanitizer, fault_type) = parse_summary(&text)?;
        let full_stack_details = parse_call_stack(&text).unwrap_or_else(|_| Default::default());
        let (scariness_score, scariness_description) = parse_scariness(&text)?;

        let call_stack = full_stack_details.iter().map(|x| x.line.clone()).collect();
        let stack_filter = get_stack_filter()?;

        let mut minimal_stack_details: Vec<StackEntry> = full_stack_details
            .iter()
            .filter_map(|x| {
                if let Some(name) = &x.function_name {
                    if stack_filter.is_match(name) {
                        return None;
                    }
                }
                Some(x.clone())
            })
            .collect();

        let llvm_test_one_input = Some(String::from("LLVMFuzzerTestOneInput"));
        if minimal_stack_details.is_empty() {
            minimal_stack_details = full_stack_details
                .iter()
                .filter_map(|x| {
                    if x.function_name == llvm_test_one_input {
                        Some(x.clone())
                    } else {
                        None
                    }
                })
                .collect();
        }

        let full_stack_names: Vec<String> = full_stack_details
            .iter()
            .filter_map(|x| x.function_name.as_ref().map(|x| x.clone()))
            .collect();

        let minimal_stack: Vec<String> = minimal_stack_details
            .iter()
            .filter_map(|x| x.function_name.as_ref().map(|x| x.clone()))
            .collect();

        let log = Self {
            text,
            sanitizer,
            summary,
            fault_type,
            call_stack,
            scariness_score,
            scariness_description,
            full_stack_details,
            full_stack_names,
            minimal_stack,
            minimal_stack_details,
        };

        Ok(log)
    }

    pub fn call_stack_sha256(&self) -> String {
        digest_iter(&self.call_stack)
    }

    pub fn minimal_stack_sha256(&self) -> String {
        digest_iter(&self.minimal_stack)
    }
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct StackEntry {
    pub line: String,
    pub address: Option<u64>,
    pub function_name: Option<String>,
    pub function_offset: Option<u64>,
    pub file_name: Option<String>,
    pub file_line: Option<u64>,
    pub module_path: Option<String>,
    pub module_offset: Option<u64>,
}

fn parse_summary(text: &str) -> Result<(String, String, String)> {
    if let Some((summary, sanitizer, fault_type)) = asan::parse_summary(&text) {
        return Ok((summary, sanitizer, fault_type));
    }
    if let Some((summary, sanitizer, fault_type)) = asan::parse_asan_runtime_error(&text) {
        return Ok((summary, sanitizer, fault_type));
    }

    bail!("unable to parse crash log summary")
}

fn parse_scariness(text: &str) -> Result<(Option<u32>, Option<String>)> {
    let (scariness_score, scariness_description) = match asan::parse_scariness(&text) {
        Some((x, y)) => (Some(x), Some(y)),
        None => (None, None),
    };
    Ok((scariness_score, scariness_description))
}

pub fn parse_call_stack(text: &str) -> Result<Vec<StackEntry>> {
    // eventually, this should be updated to support multiple callstack formats
    asan::parse_asan_call_stack(text)
}

fn digest_iter(data: impl IntoIterator<Item = impl AsRef<[u8]>>) -> String {
    let mut ctx = Sha256::new();

    for frame in data {
        ctx.update(frame);
    }

    hex::encode(ctx.finalize())
}

#[cfg(test)]
mod tests {
    use super::CrashLog;
    use anyhow::Result;
    #[test]
    fn test_asan_log_parse() -> Result<()> {
        let test_cases = vec![
            (
                "data/libfuzzer-asan-log.txt",
                "AddressSanitizer",
                "heap-use-after-free",
                7,
                None,
                None,
                vec!["LLVMFuzzerTestOneInput"],
            ),
            (
                "data/libfuzzer-deadly-signal.txt",
                "libFuzzer",
                "deadly signal",
                14,
                None,
                None,
                vec!["Json::OurReader::parse(char const*, char const*, Json::Value&, bool)", "Json::OurCharReader::parse(char const*, char const*, Json::Value*, std::__Cr::basic_string<char, std::__Cr::char_traits<char>, std::__Cr::allocator<char> >*)"],
            ),
            (
                "data/libfuzzer-windows-llvm10-out-of-memory-malloc.txt",
                "libFuzzer",
                "out-of-memory",
                16,
                None,
                None,
                vec!["__scrt_common_main_seh"],
            ),
            (
                "data/libfuzzer-windows-llvm10-out-of-memory-rss.txt",
                "libFuzzer",
                "out-of-memory",
                0,
                None,
                None,
                vec![],
            ),
            (
                "data/libfuzzer-linux-llvm10-out-of-memory-malloc.txt",
                "libFuzzer",
                "out-of-memory",
                15,
                None,
                None,
                vec!["LLVMFuzzerTestOneInput"],
            ),
            (
                "data/libfuzzer-linux-llvm10-out-of-memory-rss.txt",
                "libFuzzer",
                "out-of-memory",
                4,
                None,
                None,
                vec![],
            ),
            //(
            //    "data/tsan-linux-llvm10-data-race.txt",
            //    "ThreadSanitizer",
            //    "data race",
            //    1,
            //    None,
            //    None,
            //),
            (
                "data/clang-10-asan-breakpoint.txt",
                "AddressSanitizer",
                "breakpoint",
                43,
                None,
                None,
                vec![],
            ),
            (
                "data/asan-check-failure.txt",
                "AddressSanitizer",
                "CHECK failed",
                12,
                None,
                None,
                vec!["check", "from_file"],
            ),
            (
                "data/asan-check-failure-missing-symbolizer.txt",
                "AddressSanitizer",
                "CHECK failed",
                12,
                None,
                None,
                vec![],
            ),
            (
                "data/libfuzzer-scariness.txt",
                "AddressSanitizer",
                "FPE",
                9,
                Some(10),
                Some("signal".to_string()),
                vec!["LLVMFuzzerTestOneInput"],
            ),
            (
                "data/libfuzzer-scariness-underflow.txt",
                "AddressSanitizer",
                "stack-buffer-underflow",
                9,
                Some(51),
                Some("4-byte-write-stack-buffer-underflow".to_string()),
                vec!["LLVMFuzzerTestOneInput"],
            ),
            (
                "data/asan-odr-violation.txt",
                "AddressSanitizer",
                "odr-violation",
                2,
                None,
                None,
                vec!["asan.module_ctor"],
            ),
        ];

        for (
            log_path,
            sanitizer,
            fault_type,
            call_stack_len,
            scariness_score,
            scariness_description,
            minimal_stack,
        ) in test_cases
        {
            println!("parsing {:?}", log_path);
            let data = std::fs::read_to_string(log_path)?;
            let log = CrashLog::parse(data).unwrap();

            assert_eq!(log.sanitizer, sanitizer, "sanitizer");
            assert_eq!(log.fault_type, fault_type, "fault type");
            assert_eq!(log.call_stack.len(), call_stack_len, "call stack len");
            assert_eq!(log.scariness_score, scariness_score, "scariness");
            assert_eq!(
                log.scariness_description, scariness_description,
                "scariness description"
            );
            assert_eq!(log.minimal_stack, minimal_stack, "minimal stack");
        }
        Ok(())
    }
}
