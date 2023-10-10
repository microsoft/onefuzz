// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::too_many_arguments)]
use crate::tasks::{
    config::{CommonConfig, ContainerType},
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    report::crash_report::monitor_reports,
    stats::common::{monitor_stats, StatsFormat},
    utils::{try_resolve_setup_relative_path, CheckNotify},
};
use anyhow::{Context, Error, Result};
use onefuzz::{
    expand::{Expand, GetExpand},
    fs::{has_files, set_executable, OwnedDir},
    jitter::delay_with_jitter,
    process::monitor_process,
    syncdir::{
        SyncOperation::{Pull, Push},
        SyncedDir,
    },
};
use onefuzz_telemetry::Event::{new_coverage, new_crashdump, new_result};
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
use tokio_util::sync::CancellationToken;

use futures::TryFutureExt;

#[derive(Debug, Deserialize)]
pub struct SupervisorConfig {
    pub inputs: SyncedDir,
    pub crashes: SyncedDir,
    pub crashdumps: Option<SyncedDir>,
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
    pub coverage: Option<SyncedDir>,
    #[serde(flatten)]
    pub common: CommonConfig,
}

impl GetExpand for SupervisorConfig {
    fn get_expand(&self) -> Result<Expand<'_>> {
        Ok(self
            .common
            .get_expand()?
            .input_corpus(&self.inputs.local_path)
            .supervisor_exe(&self.supervisor_exe)
            .supervisor_options(&self.supervisor_options)
            .set_optional_ref(&self.target_exe, Expand::target_exe)
            .set_optional_ref(&self.supervisor_input_marker, |expand, input_marker| {
                expand.input_marker(input_marker)
            })
            .set_optional_ref(&self.target_options, |expand, target_options| {
                expand.target_options(target_options)
            })
            .set_optional_ref(&self.tools, |expand, tools| {
                expand.tools_dir(&tools.local_path)
            })
            .set_optional_ref(&self.coverage, |expand, coverage| {
                expand.coverage_dir(&coverage.local_path)
            })
            .set_optional_ref(&self.crashdumps, |expand, crashdumps| {
                expand.crashdumps(&crashdumps.local_path)
            })
            .set_optional_ref(&self.reports, |expand, reports| {
                expand.reports_dir(&reports.local_path)
            })
            .set_optional_ref(
                &self.crashes.remote_path.clone().and_then(|u| u.account()),
                |expand, account| expand.crashes_account(account),
            )
            .set_optional_ref(
                &self.crashes.remote_path.clone().and_then(|u| u.container()),
                |expand, container| expand.crashes_container(container),
            ))
    }
}

const HEARTBEAT_PERIOD: Duration = Duration::from_secs(60);

pub async fn spawn(config: SupervisorConfig) -> Result<(), Error> {
    let runtime_dir = OwnedDir::new(config.common.task_id.to_string());
    runtime_dir.create_if_missing().await?;

    // setup tools
    if let Some(tools) = &config.tools {
        tools.init_pull().await?;
        set_executable(&tools.local_path).await?;
    }

    // setup crashes
    let crashes = SyncedDir {
        local_path: runtime_dir.path().join("crashes"),
        remote_path: config.crashes.remote_path.clone(),
    };
    crashes.init().await?;

    let jr_client = config.common.init_job_result().await?;

    let monitor_crashes = crashes.monitor_results(new_result, false, &jr_client);

    // setup crashdumps
    let (crashdump_dir, monitor_crashdumps) = {
        let crashdump_dir = if let Some(crashdumps) = &config.crashdumps {
            let dir = SyncedDir {
                local_path: runtime_dir.path().join("crashdumps"),
                remote_path: crashdumps.remote_path.clone(),
            };
            dir.init().await?;
            Some(dir)
        } else {
            None
        };

        let monitor_dir = crashdump_dir.clone();
        let monitor_jr_client = config.common.init_job_result().await?;
        let monitor_crashdumps = async move {
            if let Some(crashdumps) = monitor_dir {
                crashdumps
                    .monitor_results(new_crashdump, false, &monitor_jr_client)
                    .await
            } else {
                Ok(())
            }
        };

        (crashdump_dir, monitor_crashdumps)
    };

    // setup coverage
    if let Some(coverage) = &config.coverage {
        coverage.init_pull().await?;
    }

    let monitor_coverage_cancellation = CancellationToken::new(); // never actually cancelled, yet
    let monitor_coverage_future = monitor_coverage(
        &config.coverage,
        config.ensemble_sync_delay,
        &monitor_coverage_cancellation,
    );

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
        &jr_client,
    );

    let inputs = SyncedDir {
        local_path: runtime_dir.path().join("inputs"),
        remote_path: config.inputs.remote_path.clone(),
    };

    inputs.init().await?;
    if let Some(context) = &config.wait_for_files {
        let dir = match context {
            ContainerType::Inputs => &inputs,
        };

        let delay = std::time::Duration::from_secs(10);
        loop {
            dir.sync_pull().await?;
            if has_files(&dir.local_path).await? {
                break;
            }
            delay_with_jitter(delay).await;
        }
    }
    let monitor_inputs = inputs.monitor_results(new_coverage, false, &jr_client);
    let inputs_sync_cancellation = CancellationToken::new(); // never actually cancelled
    let inputs_sync_task =
        inputs.continuous_sync(Pull, config.ensemble_sync_delay, &inputs_sync_cancellation);

    let process = start_supervisor(
        &runtime_dir.path(),
        &config,
        &crashes,
        crashdump_dir.as_ref(),
        &inputs,
        reports_dir.path().to_path_buf(),
    )
    .await?;

    let stopped = Notify::new();
    let monitor_supervisor =
        monitor_process(process, "supervisor".to_string(), true, Some(&stopped));
    let hb = config.common.init_heartbeat(None).await?;

    let heartbeat_process = heartbeat_process(&stopped, hb);

    let monitor_path = if let Some(stats_file) = &config.stats_file {
        Some(
            Expand::new(&config.common.machine_identity)
                .machine_id()
                .runtime_dir(runtime_dir.path())
                .evaluate_value(stats_file)?,
        )
    } else {
        debug!("no stats file to monitor");
        None
    };

    let monitor_stats = monitor_stats(monitor_path, config.stats_format);

    futures::try_join!(
        heartbeat_process.map_err(|e| e.context("Failure in heartbeat")),
        monitor_supervisor.map_err(|e| e.context("Failure in monitor_supervisor")),
        monitor_stats.map_err(|e| e.context("Failure in monitor_stats")),
        monitor_crashes.map_err(|e| e.context("Failure in monitor_crashes")),
        monitor_crashdumps.map_err(|e| e.context("Failure in monitor_crashdumps")),
        monitor_inputs.map_err(|e| e.context("Failure in monitor_inputs")),
        inputs_sync_task.map_err(|e| e.context("Failure in continuous_sync_task")),
        monitor_reports_future.map_err(|e| e.context("Failure in monitor_reports_future")),
        monitor_coverage_future.map_err(|e| e.context("Failure in monitor_coverage_future")),
    )?;

    Ok(())
}

async fn monitor_coverage(
    coverage: &Option<SyncedDir>,
    ensemble_sync_delay: Option<u64>,
    cancellation_token: &CancellationToken,
) -> Result<()> {
    if let Some(coverage) = coverage {
        coverage
            .continuous_sync(Push, ensemble_sync_delay, cancellation_token)
            .await?;
    }
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
    crashdumps: Option<&SyncedDir>,
    inputs: &SyncedDir,
    reports_dir: PathBuf,
) -> Result<Child> {
    let target_exe = if let Some(target_exe) = &config.target_exe {
        Some(try_resolve_setup_relative_path(&config.common.setup_dir, target_exe).await?)
    } else {
        None
    };

    let expand = config
        .get_expand()?
        .runtime_dir(&runtime_dir)
        .crashes(&crashes.local_path)
        .input_corpus(&inputs.local_path) // Why isn't this value in the config? It's not super clear to me from looking at the calling code.
        .reports_dir(reports_dir)
        .set_optional_ref(&crashdumps, |expand, crashdumps| {
            // And this one too...
            expand.crashdumps(&crashdumps.local_path)
        })
        .set_optional_ref(&target_exe, |expand, target_exe| {
            expand.target_exe(target_exe)
        });

    let supervisor_path = expand.evaluate_value(&config.supervisor_exe)?;
    let mut cmd = Command::new(supervisor_path);
    let cmd = cmd
        .kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdin(Stdio::null())
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
        .with_context(|| format!("supervisor failed to start: {cmd:?}"))?;
    Ok(child)
}

#[cfg(test)]
mod tests {
    use onefuzz::expand::{GetExpand, PlaceHolder};
    use proptest::prelude::*;

    use crate::config_test_utils::GetExpandFields;

    use super::SupervisorConfig;

    impl GetExpandFields for SupervisorConfig {
        fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)> {
            let mut params = self.common.get_expand_fields();
            params.push((
                PlaceHolder::InputCorpus,
                dunce::canonicalize(&self.inputs.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::SupervisorExe,
                dunce::canonicalize(&self.supervisor_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::SupervisorOptions,
                self.supervisor_options.join(" "),
            ));
            if let Some(target_exe) = &self.target_exe {
                params.push((
                    PlaceHolder::TargetExe,
                    dunce::canonicalize(target_exe)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(input_marker) = &self.supervisor_input_marker {
                params.push((PlaceHolder::Input, input_marker.clone()));
            }
            if let Some(target_options) = &self.target_options {
                params.push((PlaceHolder::TargetOptions, target_options.join(" ")));
            }
            if let Some(tools) = &self.tools {
                params.push((
                    PlaceHolder::ToolsDir,
                    dunce::canonicalize(&tools.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(coverage) = &self.coverage {
                params.push((
                    PlaceHolder::CoverageDir,
                    dunce::canonicalize(&coverage.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(crashdumps) = &self.crashdumps {
                params.push((
                    PlaceHolder::Crashdumps,
                    dunce::canonicalize(&crashdumps.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(reports) = &self.reports {
                params.push((
                    PlaceHolder::ReportsDir,
                    dunce::canonicalize(&reports.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(account) = &self.crashes.remote_path.clone().and_then(|u| u.account()) {
                params.push((PlaceHolder::CrashesAccount, account.clone()));
            }
            if let Some(container) = &self.crashes.remote_path.clone().and_then(|u| u.container()) {
                params.push((PlaceHolder::CrashesContainer, container.clone()));
            }

            params
        }
    }

    config_test!(SupervisorConfig);

    #[cfg(target_os = "linux")]
    mod linux {
        use super::super::*;
        use crate::tasks::stats::afl::read_stats;
        use onefuzz::blob::BlobContainerUrl;
        use onefuzz::machine_id::MachineIdentity;
        use onefuzz::process::monitor_process;
        use onefuzz_telemetry::EventData;
        use reqwest::Url;
        use std::collections::HashMap;
        use std::env;
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
        #[cfg_attr(not(feature = "integration_test"), ignore)]
        async fn test_fuzzer_linux() {
            let runtime_dir = tempfile::tempdir().unwrap();

            let supervisor_exe = if let Ok(x) = env::var("ONEFUZZ_TEST_AFL_LINUX_FUZZER") {
                x
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
            let crashes_local = tempfile::tempdir().unwrap().path().into();
            let crashes = SyncedDir {
                local_path: crashes_local,
                remote_path: Some(
                    BlobContainerUrl::parse(Url::from_directory_path(fault_dir_temp).unwrap())
                        .unwrap(),
                ),
            };

            let crashdumps_dir_temp = tempfile::tempdir().unwrap();
            let crashdumps_local = tempfile::tempdir().unwrap().path().into();
            let crashdumps = SyncedDir {
                local_path: crashdumps_local,
                remote_path: Some(
                    BlobContainerUrl::parse(Url::from_directory_path(crashdumps_dir_temp).unwrap())
                        .unwrap(),
                ),
            };

            let corpus_dir_local = tempfile::tempdir().unwrap().path().into();
            let corpus_dir_temp = tempfile::tempdir().unwrap();
            let corpus_dir = SyncedDir {
                local_path: corpus_dir_local,
                remote_path: Some(
                    BlobContainerUrl::parse(Url::from_directory_path(corpus_dir_temp).unwrap())
                        .unwrap(),
                ),
            };
            let seed_file_name = corpus_dir.local_path.join("seed.txt");
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
                inputs: corpus_dir.clone(),
                crashes: crashes.clone(),
                crashdumps: Some(crashdumps.clone()),
                tools: None,
                wait_for_files: None,
                stats_file: None,
                stats_format: None,
                ensemble_sync_delay: None,
                reports: None,
                unique_reports: None,
                no_repro: None,
                coverage: None,
                common: CommonConfig {
                    job_id: Default::default(),
                    task_id: Default::default(),
                    instance_id: Default::default(),
                    heartbeat_queue: Default::default(),
                    job_result_queue: Default::default(),
                    instance_telemetry_key: Default::default(),
                    microsoft_telemetry_key: Default::default(),
                    logs: Default::default(),
                    setup_dir: Default::default(),
                    extra_setup_dir: Default::default(),
                    extra_output: Default::default(),
                    min_available_memory_mb: Default::default(),
                    machine_identity: MachineIdentity {
                        machine_id: uuid::Uuid::new_v4(),
                        machine_name: "test".to_string(),
                        scaleset_name: None,
                    },
                    tags: Default::default(),
                    from_agent_to_task_endpoint: "/".to_string(),
                    from_task_to_agent_endpoint: "/".to_string(),
                },
            };

            let process = start_supervisor(
                runtime_dir,
                &config,
                &crashes,
                Some(&crashdumps),
                &corpus_dir,
                reports_dir,
            )
            .await
            .unwrap();

            let notify = Notify::new();
            let _fuzzing_monitor =
                monitor_process(process, "supervisor".to_string(), false, Some(&notify));
            let stat_output = crashes.local_path.join("fuzzer_stats");
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
                tokio::time::sleep(std::time::Duration::from_secs(1)).await;
            }
        }
    }
}
