// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use std::{collections::HashMap, hash::BuildHasher, path::Path};
use tokio::fs;

pub use stacktrace_parser::CrashLog;
const ASAN_LOG_TRUNCATE_SIZE: usize = 4096;

#[cfg(target_family = "windows")]
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

#[cfg(target_family = "unix")]
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

pub async fn check_asan_string(mut data: String) -> Result<Option<CrashLog>> {
    match CrashLog::parse(data.clone()) {
        Ok(log) => Ok(Some(log)),
        Err(err) => {
            if data.len() > ASAN_LOG_TRUNCATE_SIZE {
                data.truncate(ASAN_LOG_TRUNCATE_SIZE);
                data.push_str("...<truncated>");
            }
            warn!(
                "unable to parse asan log from string.  error:{:?} data:{:?}",
                err, data
            );
            Ok(None)
        }
    }
}

pub async fn check_asan_path(asan_dir: &Path) -> Result<Option<CrashLog>> {
    let mut entries = fs::read_dir(asan_dir).await?;
    // there should be only up to one file in asan_dir
    if let Some(file) = entries.next_entry().await? {
        let asan_bytes = fs::read(file.path())
            .await
            .with_context(|| format!("unable to read ASAN log: {}", file.path().display()))?;
        let mut asan_text = String::from_utf8_lossy(&asan_bytes).to_string();

        let asan = CrashLog::parse(asan_text.clone()).with_context(|| {
            if asan_text.len() > ASAN_LOG_TRUNCATE_SIZE {
                asan_text.truncate(ASAN_LOG_TRUNCATE_SIZE);
                asan_text.push_str("...<truncated>");
            }
            format_err!("unable to parse asan log {}: {:?}")
        })?;
        return Ok(Some(asan));
    }

    Ok(None)
}
