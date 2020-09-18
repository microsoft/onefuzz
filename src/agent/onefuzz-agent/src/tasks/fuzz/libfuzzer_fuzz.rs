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
    libfuzzer::{LibFuzzer, LibFuzzerLine},
    monitor::DirectoryMonitor,
    system,
    telemetry::{
        Event::{new_coverage, new_result, process_stats, runtime_stats},
        EventData,
    },
    uploader::BlobUploader,
};
use serde::Deserialize;
use std::{collections::HashMap, path::PathBuf, process::ExitStatus};
use tokio::{
    io,
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
        let hb_client = self.config.common.init_heartbeat();

        // To be scheduled.
        let resync = self.resync_all_corpuses();
        let new_corpus = self.monitor_new_corpus();
        let faults = self.monitor_faults();

        let (stats_sender, stats_receiver) = mpsc::unbounded_channel();
        let report_stats = report_runtime_stats(workers as usize, stats_receiver, hb_client);

        let fuzzers: Vec<_> = (0..workers)
            .map(|id| self.start_fuzzer_monitor(id, stats_sender.clone()))
            .collect();

        let fuzzers = try_join_all(fuzzers);

        futures::try_join!(resync, new_corpus, faults, fuzzers, report_stats)?;

        Ok(())
    }

    // The fuzzer monitor coordinates a _series_ of fuzzer runs.
    //
    // A run is one session of continuous fuzzing, terminated by a fuzzing error
    // or discovered fault. The monitor restarts the libFuzzer when it exits.
    async fn start_fuzzer_monitor(&self, worker_id: u64, stats_sender: StatsSender) -> Result<()> {
        loop {
            let run = self.run_fuzzer(worker_id, stats_sender.clone());

            if let Err(err) = run.await {
                error!("Fuzzer run failed: {}", err);
            }
        }
    }

    // Fuzz with a libFuzzer until it exits.
    //
    // While it runs, parse stderr for progress metrics, and report them.
    async fn run_fuzzer(&self, worker_id: u64, stats_sender: StatsSender) -> Result<ExitStatus> {
        use io::AsyncBufReadExt;

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
        let mut running =
            fuzzer.fuzz(&self.config.crashes.path, &self.config.inputs.path, &inputs)?;

        let sys_info = task::spawn(report_fuzzer_sys_info(worker_id, run_id, running.id()));

        // Splitting borrow.
        let stderr = running
            .stderr
            .as_mut()
            .ok_or_else(|| format_err!("stderr not captured"))?;
        let stderr = io::BufReader::new(stderr);

        stderr
            .lines()
            .for_each(|line| {
                let stats_sender = stats_sender.clone();

                async move {
                    let line = line.map_err(|e| e.into());

                    if let Err(err) = try_report_iter_update(stats_sender, worker_id, run_id, line)
                    {
                        error!("could not parse fuzzing iteration update: {}", err);
                    }
                }
            })
            .await;

        let (exit_status, _) = tokio::join!(running, sys_info);

        Ok(exit_status?)
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

    async fn monitor_new_corpus(&self) -> Result<()> {
        let url = self.config.inputs.url.url();
        let dir = self.config.inputs.path.clone();

        let mut monitor = DirectoryMonitor::new(dir);
        monitor.start()?;

        monitor
            .for_each(move |item| {
                let url = url.clone();

                async move {
                    event!(new_coverage; EventData::Path = item.display().to_string());

                    let mut uploader = BlobUploader::new(url);

                    if let Err(err) = uploader.upload(item.clone()).await {
                        error!("Couldn't upload coverage: {}", err);
                    }
                }
            })
            .await;

        Ok(())
    }

    async fn monitor_faults(&self) -> Result<()> {
        let url = self.config.crashes.url.url();
        let dir = self.config.crashes.path.clone();

        let mut monitor = DirectoryMonitor::new(dir);
        monitor.start()?;

        monitor
            .for_each(move |item| {
                let url = url.clone();

                async move {
                    event!(new_result; EventData::Path = item.display().to_string());

                    let mut uploader = BlobUploader::new(url);

                    if let Err(err) = uploader.upload(item.clone()).await {
                        error!("Couldn't upload fault: {}", err);
                    }
                }
            })
            .await;

        Ok(())
    }
}

fn try_report_iter_update(
    stats_sender: StatsSender,
    worker_id: u64,
    run_id: Uuid,
    line: Result<String>,
) -> Result<()> {
    let line = line?;
    let line = LibFuzzerLine::parse(line)?;

    if let Some(line) = line {
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
    heartbeat_sender: impl HeartbeatSender,
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
                heartbeat_sender.alive();
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
