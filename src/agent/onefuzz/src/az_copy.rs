// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use backoff::{self, future::retry_notify, ExponentialBackoff};
use std::{
    ffi::OsStr,
    fmt,
    path::Path,
    process::Stdio,
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};
use tempfile::tempdir;
use tokio::fs;
use tokio::process::Command;

const RETRY_INTERVAL: Duration = Duration::from_secs(5);
const RETRY_COUNT: usize = 5;

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
        write!(f, "{}", as_str)
    }
}

async fn read_azcopy_log_file(path: &Path) -> Result<String> {
    let mut entries = fs::read_dir(path).await?;
    // there should be only up to one file in azcopy_log dir
    if let Some(file) = entries.next_entry().await? {
        fs::read_to_string(file.path())
            .await
            .with_context(|| format!("unable to read file: {}", file.path().display()))
    } else {
        bail!("no log file in path: {}", path.display());
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
        .arg(mode.to_string())
        .arg(&src)
        .arg(&dst)
        .arg("--log-level")
        .arg("ERROR")
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
            .unwrap_or_else(|e| format!("unable to read azcopy log file from: {:?}", e));
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

async fn retry_az_impl(mode: Mode, src: &OsStr, dst: &OsStr, args: &[&str]) -> Result<()> {
    let counter = AtomicUsize::new(0);

    let operation = || async {
        let attempt_count = counter.fetch_add(1, Ordering::SeqCst);
        let result = az_impl(mode, src, dst, args)
            .await
            .with_context(|| format!("azcopy {} attempt {} failed", mode, attempt_count + 1));
        match result {
            Ok(()) => Ok(()),
            Err(x) => {
                if attempt_count >= RETRY_COUNT {
                    Err(backoff::Error::Permanent(x))
                } else {
                    Err(backoff::Error::Transient(x))
                }
            }
        }
    };

    retry_notify(
        ExponentialBackoff {
            current_interval: RETRY_INTERVAL,
            initial_interval: RETRY_INTERVAL,
            ..ExponentialBackoff::default()
        },
        operation,
        |err, dur| warn!("request attempt failed after {:?}: {:?}", dur, err),
    )
    .await?;

    Ok(())
}

pub async fn sync(src: impl AsRef<OsStr>, dst: impl AsRef<OsStr>, delete_dst: bool) -> Result<()> {
    let args = if delete_dst {
        vec!["--delete_destination"]
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
