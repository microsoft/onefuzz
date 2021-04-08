// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use backoff::{self, future::retry_notify, ExponentialBackoff};
use std::ffi::OsStr;
use std::fmt;
use std::process::Stdio;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;
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

async fn az_impl(mode: Mode, src: &OsStr, dst: &OsStr, args: &[&str]) -> Result<()> {
    let mut cmd = Command::new("azcopy");
    cmd.kill_on_drop(true)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .arg(mode.to_string())
        .arg(&src)
        .arg(&dst)
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
        anyhow::bail!(
            "azcopy {} failed src:{:?} dst:{:?} stdout:{:?} stderr:{:?}",
            mode,
            src,
            dst,
            stdout,
            stderr
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
