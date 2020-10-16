// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::ffi::OsStr;
use tokio::process::Command;

pub async fn sync(src: impl AsRef<OsStr>, dst: impl AsRef<OsStr>) -> Result<()> {
    use std::process::Stdio;

    let mut cmd = Command::new("azcopy");

    cmd.kill_on_drop(true)
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .arg("sync")
        .arg(&src)
        .arg(&dst);

    let output = cmd.spawn()?.wait_with_output().await?;
    if !output.status.success() {
        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);
        anyhow::bail!(
            "sync failed src:{:?} dst:{:?} stdout:{:?} stderr:{:?}",
            src.as_ref(),
            dst.as_ref(),
            stdout,
            stderr
        );
    }

    Ok(())
}

pub async fn copy(src: impl AsRef<OsStr>, dst: impl AsRef<OsStr>, recursive: bool) -> Result<()> {
    use std::process::Stdio;

    let mut cmd = Command::new("azcopy");

    cmd.kill_on_drop(true)
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .arg("copy")
        .arg(&src)
        .arg(&dst);

    if recursive {
        cmd.arg("--recursive=true");
    }

    let output = cmd.spawn()?.wait_with_output().await?;
    if !output.status.success() {
        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);
        anyhow::bail!(
            "sync failed src:{:?} dst:{:?} stdout:{:?} stderr:{:?}",
            src.as_ref(),
            dst.as_ref(),
            stdout,
            stderr
        );
    }

    Ok(())
}
