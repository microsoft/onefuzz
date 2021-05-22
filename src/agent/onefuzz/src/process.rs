// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use process_control::{self, ChildExt, Timeout};
use std::path::Path;
use std::process::Command;
use std::time::Duration;
use std::{collections::HashMap, process::Stdio};
use tokio::{
    io::{AsyncBufReadExt, AsyncRead, BufReader},
    process::Child,
    sync::Notify,
};

// Chosen to be significantly below the 32k ApplicationInsights message size
const MAX_LOG_LINE_LENGTH: usize = 8192;

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
            code: status.code().map(|s| s as i32),
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
    debug!(
        "running command with timeout: cmd:{:?} argv:{:?} env:{:?} timeout:{:?}",
        program, argv, env, timeout
    );

    let mut cmd = Command::new(program);
    cmd.env_remove("RUST_LOG")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .args(argv)
        .envs(env);

    // make a stringified version to save in the context of spawn_blocking
    let program_name = program.display().to_string();

    let runner = tokio::task::spawn_blocking(move || {
        let child = cmd
            .spawn()
            .with_context(|| format!("process failed to start: {}", program_name))?;
        child
            .with_output_timeout(timeout)
            .terminating()
            .wait()?
            .ok_or_else(|| format_err!("process timed out"))
    });

    // convert processcontrol::Output into our Output
    runner.await?.map(|result| result.into())
}

async fn monitor_stream(name: &str, context: &str, stream: impl AsyncRead + Unpin) -> Result<()> {
    let mut stream = BufReader::new(stream);
    loop {
        let mut buf = vec![];

        let bytes_read = stream.read_until(b'\n', &mut buf).await?;
        if bytes_read == 0 && buf.is_empty() {
            break;
        }
        let mut line = String::from_utf8_lossy(&buf).to_string();
        if line.len() > MAX_LOG_LINE_LENGTH {
            line.truncate(MAX_LOG_LINE_LENGTH);
            line.push_str("...<truncated>");
        }

        info!("process ({}) {}: {}", name, context, line);
    }
    Ok(())
}

async fn wait_process(context: &str, process: Child, stopped: Option<&Notify>) -> Result<()> {
    debug!("waiting for child: {}", context);

    let output = process.wait_with_output().await?;

    debug!("child exited. {}:{:?}", context, output.status);
    if let Some(stopped) = stopped {
        stopped.notify_one();
    }

    if output.status.success() {
        Ok(())
    } else {
        error!("process failed: {:?}", output);
        bail!("process failed: {:?}", output);
    }
}

pub async fn monitor_process(
    mut process: Child,
    context: String,
    log_output: bool,
    stopped: Option<&Notify>,
) -> Result<()> {
    let tasks = match log_output {
        true => {
            let stderr = process
                .stderr
                .take()
                .ok_or_else(|| format_err!("stderr not captured"))?;

            let stdout = process
                .stdout
                .take()
                .ok_or_else(|| format_err!("stdout not captured"))?;

            let stdout_log = monitor_stream("stdout", &context, stdout);
            let stderr_log = monitor_stream("stderr", &context, stderr);
            Some((stdout_log, stderr_log))
        }
        false => None,
    };

    let child = wait_process(&context, process, stopped);

    if let Some((t1, t2)) = tasks {
        futures::try_join!(t1, t2, child)?;
    } else {
        child.await?;
    }

    Ok(())
}
