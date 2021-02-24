// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use libclusterfuzz::get_stack_filter;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};

mod asan;

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
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

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
pub struct CrashLog {
    pub text: String,
    pub sanitizer: String,
    pub summary: String,
    pub fault_type: String,
    pub call_stack: Vec<String>,
    pub full_stack_details: Vec<StackEntry>,
    pub full_stack_names: Vec<String>,
    pub minimized_stack_details: Vec<StackEntry>,
    pub minimized_stack: Vec<String>,
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

        let mut minimized_stack_details: Vec<StackEntry> = full_stack_details
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

        // if we don't have a minimized stack, if one of these functions is on
        // the stack, use it
        for entry in &[
            "LLVMFuzzerTestOneInput",
            "fuzzer::RunOneTest(fuzzer::Fuzzer*, char const*, unsigned long)",
            "main",
        ] {
            if !minimized_stack_details.is_empty() {
                break;
            }
            let value = Some(String::from(*entry));
            minimized_stack_details = full_stack_details
                .iter()
                .filter_map(|x| {
                    if x.function_name == value {
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

        let minimized_stack: Vec<String> = minimized_stack_details
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
            minimized_stack,
            minimized_stack_details,
        };

        Ok(log)
    }

    pub fn call_stack_sha256(&self) -> String {
        digest_iter(&self.call_stack, None)
    }

    pub fn minimized_stack_sha256(&self, depth: Option<usize>) -> String {
        digest_iter(&self.minimized_stack, depth)
    }
}

fn parse_summary(text: &str) -> Result<(String, String, String)> {
    // eventually, this should be updated to support multiple callstack formats
    asan::parse_summary(&text)
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

fn digest_iter(data: impl IntoIterator<Item = impl AsRef<[u8]>>, depth: Option<usize>) -> String {
    let mut ctx = Sha256::new();

    if let Some(depth) = depth {
        for frame in data.into_iter().take(depth) {
            ctx.update(frame);
        }
    } else {
        for frame in data {
            ctx.update(frame);
        }
    }

    hex::encode(ctx.finalize())
}

#[cfg(test)]
mod tests {
    use super::CrashLog;
    use anyhow::{Context, Result};
    use pretty_assertions::assert_eq;
    use serde_json;
    use std::fs;
    use std::path::Path;

    fn check_dir(src_dir: &Path, expected_dir: &Path, skip_files: Vec<&str>) -> Result<()> {
        for entry in fs::read_dir(src_dir)? {
            let path = entry?.path();
            if !path.is_file() {
                eprintln!("only checking files: {}", path.display());
                continue;
            }

            let file_name = path.file_name().unwrap().to_str().unwrap();
            if skip_files.contains(&file_name) {
                eprintln!("skipping file: {}", file_name);
                continue;
            }

            let data =
                fs::read_to_string(&path).with_context(|| format!("reading {}", path.display()))?;
            let parsed = CrashLog::parse(data.clone()).with_context(|| {
                format!(
                    "parsing\n{}\n{}\n\n{}",
                    path.display(),
                    data,
                    path.display()
                )
            })?;

            let mut expected_path = expected_dir.join(&file_name);
            expected_path.set_extension("json");
            if !expected_path.is_file() {
                eprintln!(
                    "missing expected result: {} - {}",
                    path.display(),
                    expected_path.display()
                );
                continue;
            }

            let expected_data = fs::read_to_string(&expected_path)?;
            let expected: CrashLog = serde_json::from_str(&expected_data)?;
            assert_eq!(parsed, expected, "{}", path.display());
        }
        Ok(())
    }

    #[test]
    fn test_asan_log_parse() -> Result<()> {
        let src_dir = Path::new("data/stack-traces/");
        let expected_dir = Path::new("data/parsed-traces/");
        let skip_files = vec![];
        check_dir(src_dir, expected_dir, skip_files)?;

        Ok(())
    }

    #[test]
    fn test_clusterfuzz_traces() -> Result<()> {
        let src_dir = Path::new("../libclusterfuzz/data/stack-traces/");
        let expected_dir = Path::new("../libclusterfuzz/data/parsed-traces/");
        let skip_files = vec![
            // fuchsia libfuzzer
            "fuchsia_ignore.txt",
            // other (non-libfuzzer)
            "android_null_stack.txt",
            "android_security_dcheck_failure.txt",
            "assert_in_drt_string.txt",
            "check_failure_android_media.txt",
            "check_failure_android_media2.txt",
            "check_failure_chrome.txt",
            "check_failure_chrome_android.txt",
            "check_failure_chrome_android2.txt",
            "check_failure_chrome_mac.txt",
            "check_failure_chrome_media.txt",
            "check_failure_chrome_win.txt",
            "check_failure_with_assert_message.txt",
            "check_failure_with_comparison.txt",
            "check_failure_with_comparison2.txt",
            "check_failure_with_handle_sigill=0.txt",
            "generic_segv.txt",
            "ignore_libc_if_symbolized.txt",
            "keep_libc_if_unsymbolized.txt",
            "missing_library_android.txt",
            "missing_library_linux.txt",
            "oom.txt",
            "stack_filtering.txt",
            // java
            "java_IllegalStateException.txt",
            "java_fatal_exception.txt",
            // cdb
            "cdb_divide_by_zero.txt",
            "cdb_integer_overflow.txt",
            "cdb_other.txt",
            "cdb_read.txt",
            "cdb_read_x64.txt",
            "cdb_stack_overflow.txt",
            // gdb
            "gdb_sigtrap.txt",
            // v8 panic
            "ignore_asan_warning.txt",
            "security_check_failure.txt",
            "security_dcheck_failure.txt",
            "v8_check.txt",
            "v8_check_eq.txt",
            "v8_check_windows.txt",
            "v8_correctness_failure.txt",
            "v8_fatal_error_no_check.txt",
            "v8_fatal_error_partial.txt",
            "v8_javascript_assertion_should_pass.txt",
            "v8_oom.txt",
            "v8_representation_changer_error.txt",
            "v8_runtime_error.txt",
            "v8_unimplemented_code.txt",
            "v8_unknown_fatal_error.txt",
            "v8_unreachable_code.txt",
            // golaong
            "golang_panic_custom_short_message.txt",
            "golang_panic_runtime_error_index_out_of_range.txt",
            "golang_panic_runtime_error_integer_divide_by_zero.txt",
            "golang_panic_runtime_error_invalid_memory_address.txt",
            "golang_panic_runtime_error_makeslice_len_out_of_range.txt",
            "golang_panic_with_type_assertions_in_frames.txt",
            "golang_sigsegv_panic.txt",
            // linux kernel
            "android_kernel.txt",
            "android_kernel_no_parens.txt",
            "kasan_gpf.txt",
            "kasan_null.txt",
            "kasan_oob_read.txt",
            "kasan_syzkaller.txt",
            "kasan_syzkaller_android.txt",
            "kasan_uaf.txt",
            // ???
            "asan_in_drt_string.txt",
            // cfi check
            "cfi_bad_cast_indirect_fc.txt",
            "cfi_invalid_vtable.txt",
            "cfi_nodebug.txt",
            "cfi_unrelated_vtable.txt",
            // UBSAN - TODO - these should be handled
            "ubsan_bad_cast_downcast.txt",
            "ubsan_integer_overflow_addition.txt",
            "ubsan_non_positive_vla_bound_value.txt",
            "ubsan_null_pointer_member_call.txt",
            "ubsan_object_size.txt",
            "ubsan_pointer_overflow.txt",
            "ubsan_unsigned_integer_overflow.txt",
            // HWAddressSanitizer TODO - these should get handled
            "hwasan_tag_mismatch.txt",
        ];
        check_dir(src_dir, expected_dir, skip_files)?;

        Ok(())
    }
}
