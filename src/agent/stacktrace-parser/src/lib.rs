// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use libclusterfuzz::get_stack_filter;
use regex::RegexSet;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};

mod asan;
mod dotnet;

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct StackEntry {
    pub line: String,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub address: Option<u64>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub function_name: Option<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub function_offset: Option<u64>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub source_file_name: Option<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub source_file_path: Option<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub source_file_line: Option<u64>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub module_path: Option<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub module_offset: Option<u64>,
}

impl StackEntry {
    fn function_line_entry(&self) -> Option<String> {
        let mut parts = vec![];
        if let Some(function_name) = &self.function_name {
            parts.push(function_name.clone());
        }

        let mut source = vec![];
        if let Some(source_file_name) = &self.source_file_name {
            source.push(source_file_name.clone());
        }
        if let Some(source_file_line) = self.source_file_line {
            source.push(format!("{source_file_line}"));
        }
        if let Some(function_offset) = self.function_offset {
            source.push(format!("{function_offset}"));
        }

        if !source.is_empty() {
            parts.push(source.join(":"));
        }

        if parts.is_empty() {
            None
        } else {
            Some(parts.join(" "))
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct CrashLog {
    pub text: Option<String>,
    pub sanitizer: String,
    pub summary: String,
    pub fault_type: String,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub call_stack: Vec<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub full_stack_details: Vec<StackEntry>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub full_stack_names: Vec<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub minimized_stack_details: Vec<StackEntry>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub minimized_stack: Vec<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub minimized_stack_function_names: Vec<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub minimized_stack_function_lines: Vec<String>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scariness_score: Option<u32>,
    #[serde(default)]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scariness_description: Option<String>,
}

fn function_without_args(func: &str) -> String {
    // TODO: This is an extremely niave approach to splitting arguments. It
    // doesn't handle C++ well.  As an example:
    //
    // This turns this:
    // "base::internal::RunnableAdapter<void (__cdecl*)(scoped_ptr<blink::WebTaskRunner::Task,std::default_delete<blink::WebTaskRunner::Task> >)>::Run",
    //
    // Into this:
    // "base::internal::RunnableAdapter<void "
    //
    // However, given this is intended to enable de-duplicating crash reports
    // with ClusterFuzz, this is going to stay this way for now. This is used to
    // fill in `minimized_stack_functions_names`. The unstripped version will be
    // in `minimized_stack`.
    func.split_once('(')
        .map(|(x, _)| x)
        .unwrap_or(func)
        .to_string()
}

fn filter_funcs(entry: &StackEntry, stack_filter: &RegexSet) -> Option<StackEntry> {
    let mut entry = entry.clone();
    if let Some(name) = &entry.function_name {
        // mirror Clusterfuzz's replacing LLVMFuzzerTestOneInput
        // with the fuzzer filename
        //
        // Ref: https://github.com/google/clusterfuzz/blob/
        //    a6bb73e4988f4a7064e990a0a78cc6bc812ef741/src/python/
        //    lib/clusterfuzz/stacktraces/__init__.py#L1362-L1373
        if name == "LLVMFuzzerTestOneInput" {
            if let Some(file_name) = &entry.source_file_name {
                entry.function_name = Some(file_name.to_string());
                return Some(entry);
            }
        }
        if stack_filter.is_match(name) {
            return None;
        }
    }

    if let Some(name) = &entry.module_path {
        if stack_filter.is_match(name) {
            return None;
        }
    }

    Some(entry)
}

impl CrashLog {
    pub fn new(
        text: Option<String>,
        summary: Option<String>,
        sanitizer: String,
        fault_type: String,
        scariness_score: Option<u32>,
        scariness_description: Option<String>,
        stack: Vec<StackEntry>,
    ) -> Result<Self> {
        let stack_filter = get_stack_filter();
        let mut minimized_stack_details: Vec<StackEntry> = stack
            .iter()
            .filter_map(|x| filter_funcs(x, stack_filter))
            .collect();
        // if we don't have a minimized stack, if one of these functions is on
        // the stack, use it
        for entry in [
            "LLVMFuzzerTestOneInput",
            "fuzzer::RunOneTest(fuzzer::Fuzzer*, char const*, unsigned long)",
            "main",
        ] {
            if !minimized_stack_details.is_empty() {
                break;
            }
            let value = Some(entry.to_string());
            minimized_stack_details = stack
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

        let call_stack = stack_lines(&stack);
        let full_stack_names = stack_names(&stack);

        let minimized_stack = stack_lines(&minimized_stack_details);
        let minimized_stack_function_names = stack_names(&minimized_stack_details);
        let minimized_stack_function_lines = stack_function_lines(&minimized_stack_details);

        // if summary was not supplied,
        // use first line of minimized stack
        // or else first line of stack,
        // or else nothing
        let summary = summary
            .or_else(|| minimized_stack.first().cloned())
            .or_else(|| call_stack.first().cloned())
            .unwrap_or_else(|| "<crash site unavailable>".to_string());

        Ok(Self {
            text,
            sanitizer,
            summary,
            fault_type,
            call_stack,
            scariness_score,
            scariness_description,
            full_stack_details: stack,
            full_stack_names,
            minimized_stack,
            minimized_stack_function_names,
            minimized_stack_details,
            minimized_stack_function_lines,
        })
    }

    pub fn parse(text: String) -> Result<Self> {
        let summary = parse_summary(&text)?;
        let stack = parse_call_stack(&text).unwrap_or_default();
        let (scariness_score, scariness_description) = parse_scariness(&text);
        Self::new(
            Some(text),
            Some(summary.summary),
            summary.sanitizer,
            summary.fault_type,
            scariness_score,
            scariness_description,
            stack,
        )
    }

    pub fn call_stack_sha256(&self) -> String {
        digest_iter(&self.call_stack, None)
    }

    pub fn minimized_stack_sha256(&self, depth: Option<usize>) -> String {
        digest_iter(&self.minimized_stack, depth)
    }

    pub fn minimized_stack_function_names_sha256(&self, depth: Option<usize>) -> String {
        digest_iter(&self.minimized_stack_function_names, depth)
    }

    pub fn minimized_stack_function_lines_sha256(&self, depth: Option<usize>) -> String {
        digest_iter(&self.minimized_stack_function_lines, depth)
    }
}

fn stack_lines(stack: &[StackEntry]) -> Vec<String> {
    stack.iter().map(|x| x.line.clone()).collect()
}

fn stack_names(stack: &[StackEntry]) -> Vec<String> {
    stack
        .iter()
        .filter_map(|x| x.function_name.as_ref())
        .map(|x| function_without_args(x))
        .collect()
}

fn stack_function_lines(stack: &[StackEntry]) -> Vec<String> {
    stack.iter().flat_map(|x| x.function_line_entry()).collect()
}

struct CrashLogSummary {
    summary: String,
    sanitizer: String,
    fault_type: String,
}

fn parse_summary(text: &str) -> Result<CrashLogSummary> {
    // eventually, this should be updated to support multiple callstack formats

    // dotnet should be parsed first to try to extract a .NET exception stack trace
    // since this is a specialization of an ASAN dump
    dotnet::parse_summary(text)
        .or_else(|| asan::parse_summary(text))
        .ok_or(anyhow::format_err!("unable to parse crash log summary"))
}

fn parse_scariness(text: &str) -> (Option<u32>, Option<String>) {
    // eventually, this should be updated to support multiple callstack formats,
    // including building this value
    match asan::parse_scariness(text) {
        Some((x, y)) => (Some(x), Some(y)),
        None => (None, None),
    }
}

pub fn parse_call_stack(text: &str) -> Result<Vec<StackEntry>> {
    // eventually, this should be updated to support multiple callstack formats

    // if we find a .NET callstack and an ASAN callstack, splat the .NET one on top:
    let mut dotnet_callstack = dotnet::parse_dotnet_callstack(text);
    let asan_callstack = asan::parse_asan_call_stack(text)?;
    dotnet_callstack.extend(asan_callstack.into_iter());
    Ok(dotnet_callstack)
}

pub fn digest_iter(
    data: impl IntoIterator<Item = impl AsRef<[u8]>>,
    depth: Option<usize>,
) -> String {
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
    use anyhow::Context;
    use std::ffi::OsStr;
    use std::fs;

    fn check_dir(
        src_dir: &str,
        expected_dir: &str,
        skip_files: &[&OsStr],
        skip_minimized_check: &[&OsStr],
    ) {
        insta::glob!(src_dir, "*.txt", |path| {
            let file_name = path.file_name().unwrap();
            if skip_files.contains(&file_name) {
                eprintln!("skipping file: {path:?}");
                return;
            }

            let data_raw = fs::read_to_string(path)
                .with_context(|| format!("reading {}", path.display()))
                .unwrap();

            let data = data_raw.replace("\r\n", "\n");
            let parsed = CrashLog::parse(data.clone())
                .with_context(|| {
                    format!(
                        "parsing\n{}\n{}\n\n{}",
                        path.display(),
                        data,
                        path.display()
                    )
                })
                .unwrap();

            if !skip_minimized_check.contains(&file_name)
                && !parsed.call_stack.is_empty()
                && !parsed.full_stack_names.is_empty()
            {
                assert!(
                    !parsed.minimized_stack.is_empty(),
                    "minimized call stack got reduced to nothing {}",
                    path.display()
                );
            }

            insta::with_settings!({ prepend_module_to_snapshot => false, snapshot_path => expected_dir }, {
                insta::assert_json_snapshot!(parsed);
            });
        });
    }

    #[test]
    fn test_asan_log_parse() {
        let src_dir = "../data/stack-traces";
        let expected_dir = "../data/parsed-traces";
        let skip_files = [];
        let skip_minimized_check = ["asan-odr-violation.txt"].map(OsStr::new);

        check_dir(src_dir, expected_dir, &skip_files, &skip_minimized_check);
    }

    #[test]
    fn test_clusterfuzz_traces() {
        let src_dir = "../../libclusterfuzz/data/stack-traces";
        let expected_dir = "../../libclusterfuzz/data/parsed-traces";

        let skip_files = [
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
            // golang
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
            // TODO - needs fixed
            "android_asan_uaf.txt",
            // java (from jazzer)
            "java_severity_medium_exception.txt",
        ]
        .map(OsStr::new);

        let skip_minimized_check = [
            "clang-10-asan-breakpoint.txt",
            "asan-check-failure-missing-symbolizer.txt",
            // TODO: handle seeing LLVMFuzzerTestOneInput but not seeing the
            // source file name
            "libfuzzer_deadly_signal.txt",
            "lsan_direct_leak.txt",
            // TODO: address these:
            "fuchsia_ignore.txt",
            "fuchsia_reproducible_crash.txt",
            // TODO: add parsing for golang traces
            "golang_fatal_error_stack_overflow.txt",
            "golang_generic_fatal_error_and_asan_abrt.txt",
            "golang_generic_panic_and_asan_abrt.txt",
            "golang_new_crash_type_and_asan_abrt.txt",
            "golang_panic_runtime_error_index_out_of_range_with_msan.txt",
            "golang_asan_panic.txt",
            "golang_panic_runtime_error_slice_bounds_out_of_range.txt",
            "v8_check_symbolized.txt",
            "v8_dcheck_symbolized.txt",
            // TODO - needs fixed, multi-line ASAN entry
            //"sanitizer_signal_abrt_unknown.txt",
        ]
        .map(OsStr::new);

        check_dir(src_dir, expected_dir, &skip_files, &skip_minimized_check);
    }
}
