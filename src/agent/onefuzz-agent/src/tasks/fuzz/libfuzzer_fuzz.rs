// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{config::CommonConfig, heartbeat::HeartbeatSender, utils::default_bool_true};
use anyhow::{Context, Result};
use futures::{future::try_join_all, stream::StreamExt};
use onefuzz::{
    fs::list_files,
    libfuzzer::{LibFuzzer, LibFuzzerLine},
    process::ExitStatus,
    syncdir::{continuous_sync, SyncOperation::Pull, SyncedDir},
    system,
    telemetry::{
        Event::{new_coverage, new_result, process_stats, runtime_stats},
        EventData,
    },
};
use serde::Deserialize;
use std::{collections::HashMap, path::PathBuf};
use tempfile::tempdir;
use tokio::{
    fs::rename,
    io::{AsyncBufReadExt, BufReader},
    sync::mpsc,
    task,
    time::{self, Duration},
};
use uuid::Uuid;

// Delay to allow for observation of CPU usage when reporting proc info.
const PROC_INFO_COLLECTION_DELAY: Duration = Duration::from_secs(1);

// Period of reporting proc info about running processes.
const PROC_INFO_PERIOD: Duration = Duration::from_secs(30);

// Period of reporting fuzzer-generated runtime stats.
const RUNTIME_STATS_PERIOD: Duration = Duration::from_secs(60);

pub fn default_workers() -> u64 {
    let cpus = num_cpus::get() as u64;
    u64::max(1, cpus - 1)
}

#[derive(Debug, Deserialize, Clone)]
pub struct Config {
    pub inputs: SyncedDir,
    pub readonly_inputs: Option<Vec<SyncedDir>>,
    pub crashes: SyncedDir,
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,

    #[serde(default = "default_workers")]
    pub target_workers: u64,
    pub ensemble_sync_delay: Option<u64>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

    #[serde(default = "default_bool_true")]
    pub expect_crash_on_failure: bool,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct LibFuzzerFuzzTask {
    config: Config,
}

impl LibFuzzerFuzzTask {
    pub fn new(config: Config) -> Result<Self> {
        Ok(Self { config })
    }

    fn workers(&self) -> u64 {
        match self.config.target_workers {
            0 => default_workers(),
            x => x,
        }
    }

    pub async fn run(&self) -> Result<()> {
        self.init_directories().await?;

        let hb_client = self.config.common.init_heartbeat().await?;

        // To be scheduled.
        let resync = self.continuous_sync_inputs();
        let new_inputs = self.config.inputs.monitor_results(new_coverage);
        let new_crashes = self.config.crashes.monitor_results(new_result);

        let (stats_sender, stats_receiver) = mpsc::unbounded_channel();
        let report_stats = report_runtime_stats(self.workers() as usize, stats_receiver, hb_client);
        let fuzzers = self.run_fuzzers(Some(&stats_sender));
        futures::try_join!(resync, new_inputs, new_crashes, fuzzers, report_stats)?;

        Ok(())
    }

    pub async fn run_fuzzers(&self, stats_sender: Option<&StatsSender>) -> Result<()> {
        let fuzzers: Vec<_> = (0..self.workers())
            .map(|id| self.start_fuzzer_monitor(id, stats_sender))
            .collect();

        try_join_all(fuzzers).await?;

        Ok(())
    }

    // The fuzzer monitor coordinates a _series_ of fuzzer runs.
    //
    // A run is one session of continuous fuzzing, terminated by a fuzzing error
    // or discovered fault. The monitor restarts the libFuzzer when it exits.
    pub async fn start_fuzzer_monitor(
        &self,
        worker_id: u64,
        stats_sender: Option<&StatsSender>,
    ) -> Result<()> {
        let local_input_dir = tempdir()?;
        loop {
            self.run_fuzzer(&local_input_dir.path(), worker_id, stats_sender)
                .await?;

            let mut entries = tokio::fs::read_dir(local_input_dir.path()).await?;
            while let Some(Ok(entry)) = entries.next().await {
                let destination_path = self.config.inputs.path.clone().join(entry.file_name());
                tokio::fs::rename(&entry.path(), &destination_path)
                    .await
                    .with_context(|| {
                        format!(
                            "unable to move crashing input into results directory: {} - {}?",
                            entry.path().display(),
                            destination_path.display()
                        )
                    })?;
            }
        }
    }

    // Fuzz with a libFuzzer until it exits.
    //
    // While it runs, parse stderr for progress metrics, and report them.
    async fn run_fuzzer(
        &self,
        local_inputs: impl AsRef<std::path::Path>,
        worker_id: u64,
        stats_sender: Option<&StatsSender>,
    ) -> Result<()> {
        let crash_dir = tempdir()?;
        let run_id = Uuid::new_v4();

        info!("starting fuzzer run, run_id = {}", run_id);

        let mut inputs = vec![&self.config.inputs.path];
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            readonly_inputs.iter().for_each(|d| inputs.push(&d.path));
        }

        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.common.setup_dir,
        );
        let mut running = fuzzer.fuzz(crash_dir.path(), local_inputs, &inputs)?;

        let sys_info = task::spawn(report_fuzzer_sys_info(worker_id, run_id, running.id()));

        // Splitting borrow.
        let stderr = running
            .stderr
            .as_mut()
            .ok_or_else(|| format_err!("stderr not captured"))?;
        let mut stderr = BufReader::new(stderr);

        let mut libfuzzer_output = Vec::new();
        loop {
            let mut buf = vec![];
            let bytes_read = stderr.read_until(b'\n', &mut buf).await?;
            if bytes_read == 0 && buf.is_empty() {
                break;
            }
            let line = String::from_utf8_lossy(&buf).to_string();
            if let Some(stats_sender) = stats_sender {
                if let Err(err) = try_report_iter_update(stats_sender, worker_id, run_id, &line) {
                    error!("could not parse fuzzing interation update: {}", err);
                }
            }
            libfuzzer_output.push(line);
        }

        let (exit_status, _) = tokio::join!(running, sys_info);
        let exit_status: ExitStatus = exit_status?.into();

        let files = list_files(crash_dir.path()).await?;

        // If the target exits, crashes are required unless
        // 1. Exited cleanly (happens with -runs=N)
        // 2. expect_crash_on_failure is disabled
        if files.is_empty() && !exit_status.success {
            if self.config.expect_crash_on_failure {
                bail!(
                    "libfuzzer exited without generating crashes.  status:{} stderr:{:?}",
                    serde_json::to_string(&exit_status)?,
                    libfuzzer_output.join("\n")
                );
            } else {
                warn!(
                    "libfuzzer exited without generating crashes, continuing.  status:{} stderr:{:?}",
                    serde_json::to_string(&exit_status)?,
                    libfuzzer_output.join("\n")
                );
            }
        }

        for file in &files {
            if let Some(filename) = file.file_name() {
                let dest = self.config.crashes.path.join(filename);
                rename(file, dest).await?;
            }
        }

        Ok(())
    }

    async fn init_directories(&self) -> Result<()> {
        self.config.inputs.init().await?;
        self.config.crashes.init().await?;
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            for dir in readonly_inputs {
                dir.init().await?;
            }
        }
        Ok(())
    }

    async fn continuous_sync_inputs(&self) -> Result<()> {
        let mut dirs = vec![self.config.inputs.clone()];
        if let Some(inputs) = &self.config.readonly_inputs {
            let inputs = inputs.clone();
            dirs.extend(inputs);
        }
        continuous_sync(&dirs, Pull, self.config.ensemble_sync_delay).await
    }
}

fn try_report_iter_update(
    stats_sender: &StatsSender,
    worker_id: u64,
    run_id: Uuid,
    line: &str,
) -> Result<()> {
    if let Some(line) = LibFuzzerLine::parse(line)? {
        stats_sender.send(RuntimeStats {
            worker_id,
            run_id,
            count: line.iters(),
            execs_sec: line.execs_sec(),
        })?;
    }

    Ok(())
}

async fn report_fuzzer_sys_info(worker_id: u64, run_id: Uuid, fuzzer_pid: u32) -> Result<()> {
    loop {
        system::refresh()?;

        // Allow for sampling CPU usage.
        time::delay_for(PROC_INFO_COLLECTION_DELAY).await;

        if let Some(proc_info) = system::proc_info(fuzzer_pid)? {
            event!(process_stats;
               EventData::WorkerId = worker_id,
               EventData::RunId = run_id,
               EventData::Name = proc_info.name,
               EventData::Pid = proc_info.pid,
               EventData::ProcessStatus = proc_info.status,
               EventData::CpuUsage = proc_info.cpu_usage,
               EventData::PhysicalMemory = proc_info.memory_kb,
               EventData::VirtualMemory = proc_info.virtual_memory_kb
            );
        } else {
            // The process no longer exists.
            break;
        }

        time::delay_for(PROC_INFO_PERIOD).await;
    }

    Ok(())
}

#[derive(Clone, Copy, Debug)]
pub struct RuntimeStats {
    worker_id: u64,
    run_id: Uuid,
    count: u64,
    execs_sec: f64,
}

impl RuntimeStats {
    pub fn report(&self) {
        event!(
            runtime_stats;
            EventData::WorkerId = self.worker_id,
            EventData::RunId = self.run_id,
            EventData::Count = self.count,
            EventData::ExecsSecond = self.execs_sec
        );
    }
}

type StatsSender = mpsc::UnboundedSender<RuntimeStats>;

#[derive(Clone, Copy, Debug)]
struct Timer {
    interval: Duration,
}

impl Timer {
    pub fn new(interval: Duration) -> Self {
        Self { interval }
    }

    async fn wait(&self) {
        time::delay_for(self.interval).await;
    }
}

// Report runtime stats, as delivered via the `stats` channel, with a periodic trigger to
// guarantee a minimum reporting frequency.
//
// The minimum frequency is to aid metric visualization. The libFuzzer binary runtime's `pulse`
// event is triggered by a doubling of the last (locally) logged total iteration count. For long-
// running worker runs, this can result in misleading gaps and binning artifacts. In effect, we
// are approximating nearest-neighbor interpolation on the runtime stats time series.
async fn report_runtime_stats(
    workers: usize,
    mut stats_channel: mpsc::UnboundedReceiver<RuntimeStats>,
    heartbeat_client: impl HeartbeatSender,
) -> Result<()> {
    // Cache the last-reported stats for a given worker.
    //
    // When logging stats, the most recently reported runtime stats will be used for any
    // missing data. For time-triggered logging, it will be used for all workers.
    let mut last_reported: Vec<Option<RuntimeStats>> =
        std::iter::repeat(None).take(workers).collect();

    let timer = Timer::new(RUNTIME_STATS_PERIOD);

    loop {
        tokio::select! {
            Some(stats) = stats_channel.next() => {
                heartbeat_client.alive();
                stats.report();

                let idx = stats.worker_id as usize;
                last_reported[idx] = Some(stats);
            }
            _ = timer.wait() => {
                for stats in &last_reported {
                    if let Some(stats) = stats {
                        stats.report();
                    }
                }
            }
        };
    }
}
