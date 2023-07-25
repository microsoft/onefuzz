// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::blob::url::redact_query_sas_sig;
use anyhow::{Context, Result};
use backoff::{self, future::retry_notify, ExponentialBackoff};
use std::{
    ffi::{OsStr, OsString},
    fmt,
    path::Path,
    process::Stdio,
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};
use tempfile::tempdir;
use tokio::fs;
use tokio::process::Command;
use url::Url;

const RETRY_INTERVAL: Duration = Duration::from_secs(5);
const MAX_FAILURE_COUNT: usize = 5;
const MAX_RETRY_COUNT: usize = 10;

const ALWAYS_RETRY_ERROR_STRINGS: &[&str] = &[
    // There isn't an ergonomic method to sync between the OneFuzz agent and fuzzers generating
    // data.  As such, we should always retry azcopy commands that fail with errors that occur due
    // to the fuzzers writing files while a sync is occurring.
    // ref: https://github.com/microsoft/onefuzz/issues/1189
    "source modified during transfer",
];

#[derive(Clone, Copy)]
enum Mode {
    Copy,
    Sync,
}

impl fmt::Display for Mode {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        let as_str = match self {
            Mode::Copy => "copy",
            Mode::Sync => "sync",
        };
        write!(f, "{as_str}")
    }
}

// NOTE, this is intended to read a single file in a tempdir managed by the
// caller, rather than the default AZCOPY log location.
async fn read_azcopy_log_file(path: &Path) -> Result<String> {
    let mut entries = fs::read_dir(path).await?;
    // There should 2 files in azcopy_log dir, one is the log file,
    // the other is scanning log file (added in 10.9.0)
    while let Some(file) = entries.next_entry().await? {
        if file.path().to_string_lossy().contains("scanning") {
            continue;
        }

        return fs::read_to_string(file.path())
            .await
            .with_context(|| format!("unable to read file: {}", file.path().display()));
    }

    bail!("no log file in path: {}", path.display());
}

// attempt to redact an azcopy argument if it could possibly be a SAS URL
fn redact_azcopy_sas_arg(value: &OsStr) -> OsString {
    match value.to_str().map(Url::parse) {
        Some(Ok(url)) => redact_query_sas_sig(&url).to_string().into(),
        _ => value.to_owned(),
    }
}

async fn az_impl(mode: Mode, src: &OsStr, dst: &OsStr, args: &[&str]) -> Result<()> {
    let temp_dir = tempdir()?;

    // https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-configure#change-the-location-of-log-files
    let mut cmd = Command::new("azcopy");
    cmd.kill_on_drop(true)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .env("AZCOPY_LOG_LOCATION", temp_dir.path())
        .env("AZCOPY_CONCURRENCY_VALUE", "32")
        .env("AZCOPY_BUFFER_GB", "0.5") // Limit azcopy to just half a gig of RAM
        .arg(mode.to_string())
        .arg(src)
        .arg(dst)
        .args(args);

    let output = cmd
        .spawn()
        .context("azcopy failed to start")?
        .wait_with_output()
        .await
        .context("azcopy failed to run")?;
    if !output.status.success() {
        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);
        let logfile = read_azcopy_log_file(temp_dir.path())
            .await
            .unwrap_or_else(|e| format!("unable to read azcopy log file from: {e:?}"));

        let src = redact_azcopy_sas_arg(src);
        let dst = redact_azcopy_sas_arg(dst);

        anyhow::bail!(
            "azcopy {} failed src:{:?} dst:{:?} stdout:{:?} stderr:{:?} log:{:?}",
            mode,
            src,
            dst,
            stdout,
            stderr,
            logfile,
        );
    }

    Ok(())
}

// Work around issues where azcopy fails with an error we should consider
// "acceptable" to always retry on.
fn should_always_retry(err: &anyhow::Error) -> bool {
    let as_string = format!("{err:?}");
    for value in ALWAYS_RETRY_ERROR_STRINGS {
        if as_string.contains(value) {
            info!(
                "azcopy failed with an error that always triggers a retry: {} - {:?}",
                value, err
            );
            return true;
        }
    }
    false
}

async fn retry_az_impl(mode: Mode, src: &OsStr, dst: &OsStr, args: &[&str]) -> Result<()> {
    let attempt_counter = AtomicUsize::new(0);
    let failure_counter = AtomicUsize::new(0);

    let operation = || async {
        let attempt_count = attempt_counter.fetch_add(1, Ordering::SeqCst);
        let mut failure_count = failure_counter.load(Ordering::SeqCst);
        let result = az_impl(mode, src, dst, args).await.with_context(|| {
            format!(
                "azcopy {} attempt {} failed.  (failure {})",
                mode,
                attempt_count + 1,
                failure_count + 1
            )
        });
        match result {
            Ok(()) => Ok(()),
            Err(err) => {
                if !should_always_retry(&err) {
                    failure_count = failure_counter.fetch_add(1, Ordering::SeqCst);
                }
                if failure_count >= MAX_FAILURE_COUNT || attempt_count >= MAX_RETRY_COUNT {
                    Err(backoff::Error::Permanent(err))
                } else {
                    Err(backoff::Error::transient(err))
                }
            }
        }
    };

    retry_notify(
        ExponentialBackoff {
            current_interval: RETRY_INTERVAL,
            initial_interval: RETRY_INTERVAL,
            max_elapsed_time: None,
            ..ExponentialBackoff::default()
        },
        operation,
        |err, dur| {
            info!(
                "azcopy attempt failed after {:?}: {:?} {} {}",
                dur,
                err,
                attempt_counter.load(Ordering::SeqCst),
                failure_counter.load(Ordering::SeqCst)
            )
        },
    )
    .await
    .with_context(|| format!("azcopy failed after retrying.  mode: {mode}"))?;

    Ok(())
}

pub async fn sync(src: impl AsRef<OsStr>, dst: impl AsRef<OsStr>, delete_dst: bool) -> Result<()> {
    let args = if delete_dst {
        vec!["--delete-destination=true"]
    } else {
        vec![]
    };

    retry_az_impl(Mode::Sync, src.as_ref(), dst.as_ref(), &args).await
}

pub async fn copy(src: impl AsRef<OsStr>, dst: impl AsRef<OsStr>, recursive: bool) -> Result<()> {
    let args = if recursive {
        vec!["--recursive=true"]
    } else {
        vec![]
    };

    retry_az_impl(Mode::Copy, src.as_ref(), dst.as_ref(), &args).await
}
