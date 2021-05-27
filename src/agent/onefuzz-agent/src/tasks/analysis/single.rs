// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{config::CommonConfig, heartbeat::HeartbeatSender};
use anyhow::{Context, Result};
use onefuzz::{expand::Expand, fs::set_executable, process::monitor_process, syncdir::SyncedDir};
use serde::Deserialize;
use std::time::Duration;
use std::{collections::HashMap, path::PathBuf, process::Stdio};
use tokio::process::Command;

const INITIAL_DELAY: Duration = Duration::from_millis(1);

#[derive(Debug, Deserialize)]
pub struct Config {
    pub analyzer_exe: String,
    pub analyzer_options: Vec<String>,
    pub analyzer_env: HashMap<String, String>,

    pub target_exe: PathBuf,
    pub target_options: Vec<String>,
    pub crashes: SyncedDir,
    pub input_file: String,

    pub tools: Option<SyncedDir>,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub async fn run(config: Config) -> Result<()> {
    config
        .crashes
        .init_pull()
        .await
        .context("unable to sync crashes")?;
    if let Some(tools) = &config.tools {
        tools.init_pull().await.context("unable to sync tools")?;
        set_executable(&tools.local_path)
            .await
            .context("to set tools as executable")?;
    }

    run_tool(&config).await
}

pub async fn run_tool(config: &Config) -> Result<()> {
    let heartbeat = config.common.init_heartbeat(Some(INITIAL_DELAY)).await?;
    let expand = Expand::new()
        .target_exe(&config.target_exe)
        .target_options(&config.target_options)
        .analyzer_exe(&config.analyzer_exe)
        .analyzer_options(&config.analyzer_options)
        .crashes(&config.crashes.local_path)
        .set_optional_ref(&config.tools, |tester, key| {
            tester.tools_dir(&key.local_path)
        })
        .setup_dir(&config.common.setup_dir)
        .job_id(&config.common.job_id)
        .task_id(&config.common.task_id)
        .set_optional_ref(&config.common.microsoft_telemetry_key, |tester, key| {
            tester.microsoft_telemetry_key(&key)
        })
        .set_optional_ref(&config.common.instance_telemetry_key, |tester, key| {
            tester.instance_telemetry_key(&key)
        })
        .set_optional_ref(
            &config.crashes.remote_path.clone().and_then(|u| u.account()),
            |tester, account| tester.crashes_account(account),
        )
        .set_optional_ref(
            &config
                .crashes
                .remote_path
                .clone()
                .and_then(|u| u.container()),
            |tester, container| tester.crashes_container(container),
        );

    let input_path = expand
        .evaluate_value(format!("{{crashes}}/{}", config.input_file))
        .context("unable to expand input_path")?;
    let expand = expand.input_path(input_path);

    let analyzer_path = expand
        .evaluate_value(&config.analyzer_exe)
        .context("expanding analyzer_exe failed")?;

    loop {
        let mut cmd = Command::new(&analyzer_path);
        cmd.kill_on_drop(true)
            .env_remove("RUST_LOG")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        for arg in expand.evaluate(&config.analyzer_options)? {
            cmd.arg(arg);
        }

        for (k, v) in &config.analyzer_env {
            cmd.env(
                k,
                expand
                    .evaluate_value(v)
                    .context("expanding analyzer_env failed")?,
            );
        }

        info!("analyzing input with {:?}", cmd);
        let output = cmd
            .spawn()
            .with_context(|| format!("analyzer failed to start: {}", analyzer_path))?;

        heartbeat.alive();

        // while we monitor the runtime of the debugger, we don't fail the task if
        // the debugger exits non-zero. This frequently happens during normal use of
        // debuggers.
        monitor_process(output, "crash-repro".to_string(), true, None)
            .await
            .ok();
    }
}
