// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::too_many_arguments)]
use crate::tasks::{
    config::{CommonConfig, ContainerType},
    heartbeat::*,
    stats::common::{monitor_stats, StatsFormat},
    utils::CheckNotify,
};
use anyhow::{Error, Result};
use onefuzz::{
    expand::Expand,
    fs::{has_files, set_executable, OwnedDir},
    jitter::delay_with_jitter,
    syncdir::{SyncOperation::Pull, SyncedDir},
    telemetry::Event::new_result,
};
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
use tokio::{
    process::{Child, Command},
    sync::Notify,
};

#[derive(Debug, Deserialize)]
pub struct SupervisorConfig {
    pub inputs: SyncedDir,
    pub crashes: SyncedDir,
    pub supervisor_exe: String,
    pub supervisor_env: HashMap<String, String>,
    pub supervisor_options: Vec<String>,
    pub supervisor_input_marker: Option<String>,
    pub target_exe: PathBuf,
    pub target_options: Vec<String>,
    pub tools: SyncedDir,
    pub wait_for_files: Option<ContainerType>,
    pub stats_file: Option<String>,
    pub stats_format: Option<StatsFormat>,
    pub ensemble_sync_delay: Option<u64>,
    #[serde(flatten)]
    pub common: CommonConfig,
}

const HEARTBEAT_PERIOD: Duration = Duration::from_secs(60);

pub async fn spawn(config: SupervisorConfig) -> Result<(), Error> {
    let runtime_dir = OwnedDir::new(config.common.task_id.to_string());
    runtime_dir.create_if_missing().await?;

    config.tools.init_pull().await?;
    set_executable(&config.tools.path).await?;

    let supervisor_path = Expand::new()
        .tools_dir(&config.tools.path)
        .evaluate_value(&config.supervisor_exe)?;

    let crashes = SyncedDir {
        path: runtime_dir.path().join("crashes"),
        url: config.crashes.url.clone(),
    };

    crashes.init().await?;
    let monitor_crashes = crashes.monitor_results(new_result);

    let inputs = SyncedDir {
        path: runtime_dir.path().join("inputs"),
        url: config.inputs.url.clone(),
    };
    inputs.init().await?;

    if let Some(context) = &config.wait_for_files {
        let dir = match context {
            ContainerType::Inputs => &inputs,
        };

        let delay = std::time::Duration::from_secs(10);
        loop {
            dir.sync_pull().await?;
            if has_files(&dir.path).await? {
                break;
            }
            delay_with_jitter(delay).await;
        }
    }

    let continuous_sync_task = inputs.continuous_sync(Pull, config.ensemble_sync_delay);

    let process = start_supervisor(
        &runtime_dir.path(),
        &supervisor_path,
        &config.target_exe,
        &crashes.path,
        &inputs.path,
        &config.target_options,
        &config.supervisor_options,
        &config.supervisor_env,
        &config.supervisor_input_marker,
    )
    .await?;

    let stopped = Notify::new();
    let monitor_process = monitor_process(process, &stopped);
    let hb = config.common.init_heartbeat().await?;

    let heartbeat_process = heartbeat_process(&stopped, hb);

    let monitor_path = if let Some(stats_file) = &config.stats_file {
        Some(
            Expand::new()
                .runtime_dir(runtime_dir.path())
                .evaluate_value(stats_file)?,
        )
    } else {
        verbose!("no stats file to monitor");
        None
    };

    let monitor_stats = monitor_stats(monitor_path, config.stats_format);

    futures::try_join!(
        heartbeat_process,
        monitor_process,
        monitor_stats,
        monitor_crashes,
        continuous_sync_task,
    )?;

    Ok(())
}

async fn heartbeat_process(
    stopped: &Notify,
    heartbeat_client: Option<TaskHeartbeatClient>,
) -> Result<()> {
    while !stopped.is_notified(HEARTBEAT_PERIOD).await {
        heartbeat_client.alive();
    }
    Ok(())
}

async fn monitor_process(process: tokio::process::Child, stopped: &Notify) -> Result<()> {
    verbose!("waiting for child output...");
    let output: std::process::Output = process.wait_with_output().await?;
    verbose!("child exited with {:?}", output.status);

    if output.status.success() {
        verbose!("child status is success, notifying");
        stopped.notify();
        Ok(())
    } else {
        let err_text = String::from_utf8_lossy(&output.stderr);
        let output_text = String::from_utf8_lossy(&output.stdout);
        let message = format!("{} {}", err_text, output_text);
        error!("{}", message);
        stopped.notify();
        Err(Error::msg(message))
    }
}

async fn start_supervisor(
    runtime_dir: impl AsRef<Path>,
    supervisor_path: impl AsRef<Path>,
    target_exe: impl AsRef<Path>,
    fault_dir: impl AsRef<Path>,
    inputs_dir: impl AsRef<Path>,
    target_options: &[String],
    supervisor_options: &[String],
    supervisor_env: &HashMap<String, String>,
    supervisor_input_marker: &Option<String>,
) -> Result<Child> {
    let mut cmd = Command::new(supervisor_path.as_ref());

    let cmd = cmd
        .kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    let mut expand = Expand::new();
    expand
        .supervisor_exe(supervisor_path)
        .supervisor_options(supervisor_options)
        .crashes(fault_dir)
        .runtime_dir(runtime_dir)
        .target_exe(target_exe)
        .target_options(target_options)
        .input_corpus(inputs_dir);

    if let Some(input_marker) = supervisor_input_marker {
        expand.input_marker(input_marker);
    }

    let args = expand.evaluate(supervisor_options)?;
    cmd.args(&args);

    for (k, v) in supervisor_env {
        cmd.env(k, expand.evaluate_value(v)?);
    }

    info!("starting supervisor '{:?}'", cmd);
    let child = cmd.spawn()?;
    Ok(child)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::tasks::stats::afl::read_stats;
    use onefuzz::telemetry::EventData;
    use std::collections::HashMap;
    use std::time::Instant;

    const MAX_FUZZ_TIME_SECONDS: u64 = 120;

    async fn has_stats(path: &PathBuf) -> bool {
        if let Ok(stats) = read_stats(path).await {
            for entry in stats {
                if matches!(entry, EventData::ExecsSecond(x) if x > 0.0) {
                    return true;
                }
            }
            false
        } else {
            false
        }
    }

    #[tokio::test]
    #[cfg(target_os = "linux")]
    #[cfg_attr(not(feature = "integration_test"), ignore)]
    async fn test_fuzzer_linux() {
        use std::env;

        let runtime_dir = tempfile::tempdir().unwrap();
        let afl_fuzz_exe = if let Ok(x) = env::var("ONEFUZZ_TEST_AFL_LINUX_FUZZER") {
            x
        } else {
            warn!("Unable to test AFL integration");
            return;
        };

        let afl_test_binary = if let Ok(x) = env::var("ONEFUZZ_TEST_AFL_LINUX_TEST_BINARY") {
            x
        } else {
            warn!("Unable to test AFL integration");
            return;
        };

        let fault_dir_temp = tempfile::tempdir().unwrap();
        let fault_dir = fault_dir_temp.path();
        let corpus_dir_temp = tempfile::tempdir().unwrap();
        let corpus_dir = corpus_dir_temp.into_path();
        let seed_file_name = corpus_dir.clone().join("seed.txt");
        let target_options = vec!["{input}".to_owned()];
        let supervisor_env = HashMap::new();
        let supervisor_options: Vec<_> = vec![
            "-d",
            "-i",
            "{input_corpus}",
            "-o",
            "{crashes}",
            "--",
            "{target_exe}",
            "{target_options}",
        ]
        .iter()
        .map(|p| p.to_string())
        .collect();

        // AFL input marker
        let supervisor_input_marker = Some("@@".to_owned());

        println!(
            "testing 2: corpus_dir {:?} -- fault_dir {:?} -- seed_file_name {:?}",
            corpus_dir, fault_dir, seed_file_name
        );

        tokio::fs::write(seed_file_name, "xyz").await.unwrap();
        let process = start_supervisor(
            runtime_dir,
            PathBuf::from(afl_fuzz_exe),
            PathBuf::from(afl_test_binary),
            fault_dir,
            corpus_dir,
            &target_options,
            &supervisor_options,
            &supervisor_env,
            &supervisor_input_marker,
        )
        .await
        .unwrap();

        let notify = Notify::new();
        let _fuzzing_monitor = monitor_process(process, &notify);
        let stat_output = fault_dir.join("fuzzer_stats");
        let start = Instant::now();
        loop {
            if has_stats(&stat_output).await {
                break;
            }

            if start.elapsed().as_secs() > MAX_FUZZ_TIME_SECONDS {
                panic!(
                    "afl did not generate stats in {} seconds",
                    MAX_FUZZ_TIME_SECONDS
                );
            }
            tokio::time::delay_for(std::time::Duration::from_secs(1)).await;
        }
    }
}
