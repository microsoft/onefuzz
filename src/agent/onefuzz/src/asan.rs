// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::sha256;
use anyhow::Result;
use regex::Regex;
use std::{collections::HashMap, hash::BuildHasher, path::Path};
use tokio::{fs, stream::StreamExt};

#[derive(Clone, Debug)]
pub struct AsanLog {
    text: String,
    sanitizer: String,
    summary: String,
    fault_type: String,
    call_stack: Vec<String>,
}

impl AsanLog {
    pub fn parse(text: String) -> Option<Self> {
        let (summary, sanitizer, fault_type) = parse_summary(&text)?;
        let call_stack = parse_call_stack(&text).unwrap_or_else(Vec::default);

        let log = Self {
            text,
            sanitizer,
            summary,
            fault_type,
            call_stack,
        };

        Some(log)
    }

    pub fn text(&self) -> &str {
        &self.text
    }

    pub fn summary(&self) -> &str {
        &self.summary
    }

    pub fn fault_type(&self) -> &str {
        &self.fault_type
    }

    pub fn call_stack(&self) -> &[String] {
        &self.call_stack
    }

    pub fn call_stack_sha256(&self) -> String {
        sha256::digest_iter(self.call_stack())
    }
}

fn parse_summary(text: &str) -> Option<(String, String, String)> {
    let pattern = r"SUMMARY: ((\w+): (data race|deadly signal|[^ \n]+).*)";
    let re = Regex::new(pattern).ok()?;
    let captures = re.captures(text)?;
    let summary = captures.get(1)?.as_str().trim();
    let sanitizer = captures.get(2)?.as_str().trim();
    let fault_type = captures.get(3)?.as_str().trim();
    Some((summary.into(), sanitizer.into(), fault_type.into()))
}

fn parse_call_stack(text: &str) -> Option<Vec<String>> {
    let mut stack = vec![];
    let mut parsing_stack = false;

    for line in text.lines() {
        let line = line.trim();
        let is_frame = line.starts_with('#');

        match (parsing_stack, is_frame) {
            (true, true) => {
                stack.push(line.to_string());
            }
            (true, false) => {
                return Some(stack);
            }
            (false, true) => {
                parsing_stack = true;
                stack.push(line.to_string());
            }
            (false, false) => {
                continue;
            }
        }
    }

    None
}

#[cfg(target_os = "windows")]
pub fn add_asan_log_env<S: BuildHasher>(env: &mut HashMap<String, String, S>, asan_dir: &Path) {
    let asan_path = asan_dir.join("asan-log");
    let asan_path_as_str = asan_path.to_string_lossy();
    // ASAN_OPTIONS are naively parsed by splitting on ':'.  This results in using
    // traditional paths with drive names generate the error:
    //    AddressSanitizer: ERROR: expected '=' in ASAN_OPTIONS
    //
    // For ASAN's implementation, see FlagParser::parse_flag & FlagParser::is_space
    //      llvm-project:compiler-rt/lib/sanitizer_common/sanitizer_flag_parser.cpp
    //
    // For more info:
    // https://docs.microsoft.com/en-us/dotnet/standard/io/file-path-formats
    //
    // The regex below replaces drive letter paths with local network version of the
    // same path
    let re = regex::Regex::new(r"^(?P<d>[a-zA-Z]):\\").expect("static regex parse failed");
    let network_path = re.replace(&asan_path_as_str, "\\\\127.0.0.1\\$d$\\");
    if let Some(v) = env.get_mut("ASAN_OPTIONS") {
        let log_path = format!(":log_path={}", network_path);
        v.push_str(&log_path);
    } else {
        let log_path = format!("log_path={}", network_path);
        env.insert("ASAN_OPTIONS".to_string(), log_path);
    }
}

#[cfg(target_os = "linux")]
pub fn add_asan_log_env<S: BuildHasher>(env: &mut HashMap<String, String, S>, asan_dir: &Path) {
    let asan_path = asan_dir.join("asan-log");
    let asan_path_as_str = asan_path.to_string_lossy();
    if let Some(v) = env.get_mut("ASAN_OPTIONS") {
        let log_path = format!(":log_path={}", asan_path_as_str);
        v.push_str(&log_path);
    } else {
        let log_path = format!("log_path={}", asan_path_as_str);
        env.insert("ASAN_OPTIONS".to_string(), log_path);
    }
}

pub async fn check_asan_string(mut data: String) -> Result<Option<AsanLog>> {
    let asan = AsanLog::parse(data.clone());
    if asan.is_some() {
        return Ok(asan);
    } else {
        if data.len() > 1024 {
            data.truncate(1024);
            data.push_str("...<truncated>");
        }
        warn!("unable to parse asan log from string: {:?}", data);
        Ok(None)
    }
}

pub async fn check_asan_path(asan_dir: &Path) -> Result<Option<AsanLog>> {
    let mut entries = fs::read_dir(asan_dir).await?;
    while let Some(file) = entries.next().await {
        let file = file?;
        let mut asan_text = fs::read_to_string(file.path()).await?;
        let asan = AsanLog::parse(asan_text.clone());
        if asan.is_some() {
            return Ok(asan);
        } else {
            if asan_text.len() > 1024 {
                asan_text.truncate(1024);
                asan_text.push_str("...<truncated>");
            }
            warn!(
                "unable to parse asan log {}: {:?}",
                file.path().display(),
                asan_text
            );
        }
    }

    Ok(None)
}

#[cfg(test)]
mod tests {
    use super::AsanLog;

    #[test]
    fn test_asan_log_parse() {
        let test_cases = vec![
            (
                "data/libfuzzer-asan-log.txt",
                "AddressSanitizer",
                "heap-use-after-free",
                7,
            ),
            (
                "data/libfuzzer-deadly-signal.txt",
                "libFuzzer",
                "deadly signal",
                14,
            ),
            (
                "data/libfuzzer-windows-llvm10-out-of-memory-malloc.txt",
                "libFuzzer",
                "out-of-memory",
                16,
            ),
            (
                "data/libfuzzer-windows-llvm10-out-of-memory-rss.txt",
                "libFuzzer",
                "out-of-memory",
                0,
            ),
            (
                "data/libfuzzer-linux-llvm10-out-of-memory-malloc.txt",
                "libFuzzer",
                "out-of-memory",
                15,
            ),
            (
                "data/libfuzzer-linux-llvm10-out-of-memory-rss.txt",
                "libFuzzer",
                "out-of-memory",
                4,
            ),
            (
                "data/tsan-linux-llvm10-data-race.txt",
                "ThreadSanitizer",
                "data race",
                1,
            ),
            (
                "data/clang-10-asan-breakpoint.txt",
                "AddressSanitizer",
                "breakpoint",
                43,
            ),
        ];

        for (log_path, sanitizer, fault_type, call_stack_len) in test_cases {
            let data = std::fs::read_to_string(log_path).unwrap();
            let log = AsanLog::parse(data).unwrap();

            assert_eq!(log.sanitizer, sanitizer);
            assert_eq!(log.fault_type, fault_type);
            assert_eq!(log.call_stack.len(), call_stack_len);
        }
    }
}
