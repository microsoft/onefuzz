// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::too_many_arguments)]
use crate::tasks::{
    config::{CommonConfig, ContainerType},
    heartbeat::*,
    report::crash_report::monitor_reports,
    stats::common::{monitor_stats, StatsFormat},
    utils::CheckNotify,
};
use anyhow::{Context, Error, Result};
use onefuzz::{
    expand::Expand,
    fs::{has_files, set_executable, OwnedDir},
    jitter::delay_with_jitter,
    process::monitor_process,
    syncdir::{SyncOperation::Pull, SyncedDir},
};
use onefuzz_telemetry::Event::{new_coverage, new_result};
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
use tempfile::tempdir;
use tokio::{
    process::{Child, Command},
    sync::Notify,
};

#[derive(Debug, Deserialize, Default)]
pub struct SupervisorConfig {
    pub inputs: SyncedDir,
    pub crashes: SyncedDir,
    pub supervisor_exe: String,
    pub supervisor_env: HashMap<String, String>,
    pub supervisor_options: Vec<String>,
    pub supervisor_input_marker: Option<String>,
    pub target_exe: Option<PathBuf>,
    pub target_options: Option<Vec<String>>,
    pub tools: Option<SyncedDir>,
    pub wait_for_files: Option<ContainerType>,
    pub stats_file: Option<String>,
    pub stats_format: Option<StatsFormat>,
    pub ensemble_sync_delay: Option<u64>,
    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,
    #[serde(flatten)]
    pub common: CommonConfig,
}

const HEARTBEAT_PERIOD: Duration = Duration::from_secs(60);

pub async fn spawn(config: SupervisorConfig) -> Result<(), Error> {
    let runtime_dir = OwnedDir::new(config.common.task_id.to_string());
    runtime_dir.create_if_missing().await?;

    // setup tools
    if let Some(tools) = &config.tools {
        tools.init_pull().await?;
        set_executable(&tools.path).await?;
    }

    // setup crashes
    let crashes = SyncedDir {
        path: runtime_dir.path().join("crashes"),
        url: config.crashes.url.clone(),
    };
    crashes.init().await?;
    let monitor_crashes = crashes.monitor_results(new_result);

    // setup reports
    let reports_dir = tempdir()?;
    if let Some(unique_reports) = &config.unique_reports {
        unique_reports.init().await?;
    }
    if let Some(reports) = &config.reports {
        reports.init().await?;
    }
    if let Some(no_repro) = &config.no_repro {
        no_repro.init().await?;
    }
    let monitor_reports_future = monitor_reports(
        reports_dir.path(),
        &config.unique_reports,
        &config.reports,
        &config.no_repro,
    );

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
    let monitor_inputs = inputs.monitor_results(new_coverage);
    let continuous_sync_task = inputs.continuous_sync(Pull, config.ensemble_sync_delay);

    let process = start_supervisor(
        &runtime_dir.path(),
        &config,
        &crashes,
        &inputs,
        reports_dir.path().to_path_buf(),
    )
    .await?;

    let stopped = Notify::new();
    let monitor_supervisor =
        monitor_process(process, "supervisor".to_string(), true, Some(&stopped));
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
        monitor_supervisor,
        monitor_stats,
        monitor_crashes,
        monitor_inputs,
        continuous_sync_task,
        monitor_reports_future,
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

async fn start_supervisor(
    runtime_dir: impl AsRef<Path>,
    config: &SupervisorConfig,
    crashes: &SyncedDir,
    inputs: &SyncedDir,
    reports_dir: PathBuf,
) -> Result<Child> {
    let expand = Expand::new()
        .supervisor_exe(&config.supervisor_exe)
        .supervisor_options(&config.supervisor_options)
        .runtime_dir(&runtime_dir)
        .crashes(&crashes.path)
        .input_corpus(&inputs.path)
        .reports_dir(&reports_dir)
        .setup_dir(&config.common.setup_dir)
        .job_id(&config.common.job_id)
        .task_id(&config.common.task_id)
        .set_optional_ref(&config.tools, |expand, tools| expand.tools_dir(&tools.path))
        .set_optional_ref(&config.target_exe, |expand, target_exe| {
            expand.target_exe(target_exe)
        })
        .set_optional_ref(&config.supervisor_input_marker, |expand, input_marker| {
            expand.input_marker(input_marker)
        })
        .set_optional_ref(&config.target_options, |expand, target_options| {
            expand.target_options(target_options)
        });

    let supervisor_path = expand.evaluate_value(&config.supervisor_exe)?;
    let mut cmd = Command::new(supervisor_path);
    let cmd = cmd
        .kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    let args = expand.evaluate(&config.supervisor_options)?;
    cmd.args(&args);

    for (k, v) in &config.supervisor_env {
        cmd.env(k, expand.evaluate_value(v)?);
    }

    info!("starting supervisor '{:?}'", cmd);
    let child = cmd
        .spawn()
        .with_context(|| format!("supervisor failed to start: {:?}", cmd))?;
    Ok(child)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::tasks::stats::afl::read_stats;
    use onefuzz::process::monitor_process;
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

        let supervisor_exe = if let Ok(x) = env::var("ONEFUZZ_TEST_AFL_LINUX_FUZZER") {
            x.into()
        } else {
            warn!("Unable to test AFL integration");
            return;
        };

        let target_exe = if let Ok(x) = env::var("ONEFUZZ_TEST_AFL_LINUX_TEST_BINARY") {
            Some(x.into())
        } else {
            warn!("Unable to test AFL integration");
            return;
        };

        let reports_dir_temp = tempfile::tempdir().unwrap();
        let reports_dir = reports_dir_temp.path().into();

        let fault_dir_temp = tempfile::tempdir().unwrap();
        let crashes = SyncedDir {
            path: fault_dir_temp.path().into(),
            url: None,
        };

        let corpus_dir_temp = tempfile::tempdir().unwrap();
        let corpus_dir = SyncedDir {
            path: corpus_dir_temp.path().into(),
            url: None,
        };
        let seed_file_name = corpus_dir.path.join("seed.txt");
        tokio::fs::write(seed_file_name, "xyz").await.unwrap();

        let target_options = Some(vec!["{input}".to_owned()]);
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

        let config = SupervisorConfig {
            supervisor_exe,
            supervisor_env,
            supervisor_options,
            supervisor_input_marker,
            target_exe,
            target_options,
            ..Default::default()
        };

        let process = start_supervisor(runtime_dir, &config, &crashes, &corpus_dir, reports_dir)
            .await
            .unwrap();

        let notify = Notify::new();
        let _fuzzing_monitor =
            monitor_process(process, "supervisor".to_string(), false, Some(&notify));
        let stat_output = crashes.path.join("fuzzer_stats");
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
