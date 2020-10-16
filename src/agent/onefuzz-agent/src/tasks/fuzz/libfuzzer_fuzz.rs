// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::{CommonConfig, SyncedDir},
    heartbeat::HeartbeatSender,
    utils,
};
use anyhow::Result;
use futures::{future::try_join_all, stream::StreamExt};
use onefuzz::{
    fs::list_files,
    libfuzzer::{LibFuzzer, LibFuzzerLine},
    process::ExitStatus,
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

// Time between resync of all corpus container directories.
const RESYNC_PERIOD: Duration = Duration::from_secs(30);

// Delay to allow for observation of CPU usage when reporting proc info.
const PROC_INFO_COLLECTION_DELAY: Duration = Duration::from_secs(1);

// Period of reporting proc info about running processes.
const PROC_INFO_PERIOD: Duration = Duration::from_secs(30);

// Period of reporting fuzzer-generated runtime stats.
const RUNTIME_STATS_PERIOD: Duration = Duration::from_secs(60);

#[derive(Debug, Deserialize, Clone)]
pub struct Config {
    pub inputs: SyncedDir,
    pub readonly_inputs: Option<Vec<SyncedDir>>,
    pub crashes: SyncedDir,
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_workers: Option<u64>,

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

    pub async fn start(&self) -> Result<()> {
        let workers = self.config.target_workers.unwrap_or_else(|| {
            let cpus = num_cpus::get() as u64;
            u64::max(1, cpus - 1)
        });

        self.init_directories().await?;
        self.sync_all_corpuses().await?;
        let hb_client = self.config.common.init_heartbeat().await?;

        // To be scheduled.
        let resync = self.resync_all_corpuses();
        let new_inputs = utils::monitor_result_dir(self.config.inputs.clone(), new_coverage);
        let new_crashes = utils::monitor_result_dir(self.config.crashes.clone(), new_result);

        let (stats_sender, stats_receiver) = mpsc::unbounded_channel();
        let report_stats = report_runtime_stats(workers as usize, stats_receiver, hb_client);

        let fuzzers: Vec<_> = (0..workers)
            .map(|id| self.start_fuzzer_monitor(id, Some(&stats_sender)))
            .collect();

        let fuzzers = try_join_all(fuzzers);

        futures::try_join!(resync, new_inputs, new_crashes, fuzzers, report_stats)?;

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
        loop {
            self.run_fuzzer(worker_id, stats_sender).await?;
        }
    }

    // Fuzz with a libFuzzer until it exits.
    //
    // While it runs, parse stderr for progress metrics, and report them.
    async fn run_fuzzer(&self, worker_id: u64, stats_sender: Option<&StatsSender>) -> Result<()> {
        let crash_dir = tempdir()?;
        let run_id = Uuid::new_v4();

        info!("starting fuzzer run, run_id = {}", run_id);

        let inputs: Vec<_> = {
            if let Some(readonly_inputs) = &self.config.readonly_inputs {
                readonly_inputs.iter().map(|d| &d.path).collect()
            } else {
                vec![]
            }
        };

        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
        );
        let mut running = fuzzer.fuzz(crash_dir.path(), &self.config.inputs.path, &inputs)?;

        let sys_info = task::spawn(report_fuzzer_sys_info(worker_id, run_id, running.id()));

        // Splitting borrow.
        let stderr = running
            .stderr
            .as_mut()
            .ok_or_else(|| format_err!("stderr not captured"))?;
        let stderr = BufReader::new(stderr);

        let mut libfuzzer_output = Vec::new();
        let mut lines = stderr.lines();
        while let Some(line) = lines.next_line().await? {
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

        // ignore libfuzzer exiting cleanly without crashing, which could happen via
        // -runs=N
        if !exit_status.success && files.is_empty() {
            bail!(
                "libfuzzer exited without generating crashes.  status:{} stderr:{:?}",
                serde_json::to_string(&exit_status)?,
                libfuzzer_output.join("\n")
            );
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
        utils::init_dir(&self.config.inputs.path).await?;
        utils::init_dir(&self.config.crashes.path).await?;
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            for dir in readonly_inputs {
                utils::init_dir(&dir.path).await?;
            }
        }
        Ok(())
    }

    async fn sync_all_corpuses(&self) -> Result<()> {
        utils::sync_remote_dir(&self.config.inputs, utils::SyncOperation::Pull).await?;

        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            for corpus in readonly_inputs {
                utils::sync_remote_dir(corpus, utils::SyncOperation::Pull).await?;
            }
        }

        Ok(())
    }

    async fn resync_all_corpuses(&self) -> Result<()> {
        loop {
            time::delay_for(RESYNC_PERIOD).await;

            self.sync_all_corpuses().await?;
        }
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
