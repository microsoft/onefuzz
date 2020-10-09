// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use process_control::{self, ChildExt, Timeout};
use std::path::Path;
use std::process::Command;
use std::time::Duration;
use std::{collections::HashMap, process::Stdio};

/// Serializable representation of a process output.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct Output {
    pub exit_status: ExitStatus,
    pub stderr: String,
    pub stdout: String,
}

impl From<std::process::Output> for Output {
    fn from(output: std::process::Output) -> Self {
        let exit_status = output.status.into();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();

        Self {
            exit_status,
            stderr,
            stdout,
        }
    }
}

impl From<process_control::Output> for Output {
    fn from(output: process_control::Output) -> Self {
        let exit_status = output.status.into();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();
        Self {
            exit_status,
            stderr,
            stdout,
        }
    }
}

/// Serializable representation of a process exit status.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ExitStatus {
    pub code: Option<i32>,
    pub signal: Option<i32>,
    pub success: bool,
}

impl From<std::process::ExitStatus> for ExitStatus {
    #[cfg(target_os = "windows")]
    fn from(status: std::process::ExitStatus) -> Self {
        Self {
            code: status.code(),
            signal: None,
            success: status.success(),
        }
    }

    #[cfg(target_os = "linux")]
    fn from(status: std::process::ExitStatus) -> Self {
        use std::os::unix::process::ExitStatusExt;

        Self {
            code: status.code(),
            signal: status.signal(),
            success: status.success(),
        }
    }
}

impl From<process_control::ExitStatus> for ExitStatus {
    #[cfg(target_os = "windows")]
    fn from(status: process_control::ExitStatus) -> Self {
        Self {
            code: status.code(),
            signal: None,
            success: status.success(),
        }
    }

    #[cfg(target_os = "linux")]
    fn from(status: process_control::ExitStatus) -> Self {
        Self {
            code: status.code().map(|s| s as i32),
            signal: status.signal(),
            success: status.success(),
        }
    }
}

pub async fn run_cmd<S: ::std::hash::BuildHasher>(
    program: &Path,
    argv: Vec<String>,
    env: &HashMap<String, String, S>,
    timeout: Duration,
) -> Result<Output> {
    verbose!(
        "running command with timeout: cmd:{:?} argv:{:?} env:{:?} timeout:{:?}",
        program,
        argv,
        env,
        timeout
    );

    let mut cmd = Command::new(program);
    cmd.env_remove("RUST_LOG")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .args(argv)
        .envs(env);

    let runner = tokio::task::spawn_blocking(move || {
        let child = cmd.spawn()?;
        child
            .with_output_timeout(timeout)
            .terminating()
            .wait()?
            .ok_or_else(|| format_err!("process timed out"))
    });

    // convert processcontrol::Output into our Output
    runner.await?.map(|result| result.into())
}
