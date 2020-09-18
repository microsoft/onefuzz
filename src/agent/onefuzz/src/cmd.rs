// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use process_control::{ChildExt, Output, Timeout};
use std::path::Path;
use std::process::Command;
use std::time::Duration;
use std::{collections::HashMap, process::Stdio};

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

    runner.await?
}
