// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{config::CommonConfig, heartbeat::HeartbeatSender, utils::default_bool_true};
use anyhow::{Context, Result};
use arraydeque::{ArrayDeque, Wrapping};
use futures::future::try_join_all;
use onefuzz::{
    fs::list_files,
    libfuzzer::{LibFuzzer, LibFuzzerLine},
    process::ExitStatus,
    syncdir::{continuous_sync, SyncOperation::Pull, SyncedDir},
    system,
};
use onefuzz_telemetry::{
    Event::{new_coverage, new_result, process_stats, runtime_stats},
    EventData,
};
use serde::Deserialize;
use std::{collections::HashMap, path::PathBuf, sync::Arc};
use tempfile::{tempdir_in, TempDir};
use tokio::{
    io::{AsyncBufReadExt, BufReader},
    select,
    sync::{mpsc, Notify},
    task,
    time::{sleep, Duration, Instant},
};
use uuid::Uuid;

// Delay to allow for observation of CPU usage when reporting proc info.
const PROC_INFO_COLLECTION_DELAY: Duration = Duration::from_secs(1);

// Period of reporting proc info about running processes.
const PROC_INFO_PERIOD: Duration = Duration::from_secs(30);

// Period of reporting fuzzer-generated runtime stats.
const RUNTIME_STATS_PERIOD: Duration = Duration::from_secs(60);

// Period for minimum duration between launches of libFuzzer
const COOLOFF_PERIOD: Duration = Duration::from_secs(10);

/// Maximum number of log message to safe in case of libFuzzer failing,
/// arbitrarily chosen
const LOGS_BUFFER_SIZE: usize = 1024;

pub fn default_workers() -> usize {
    let cpus = num_cpus::get();
    usize::max(1, cpus - 1)
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
    pub target_workers: usize,
    pub ensemble_sync_delay: Option<u64>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

    #[serde(default)]
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

    fn workers(&self) -> usize {
        match self.config.target_workers {
            0 => default_workers(),
            x => x,
        }
    }

    pub async fn run(&self) -> Result<()> {
        self.init_directories().await?;
        self.verify().await?;

        let hb_client = self.config.common.init_heartbeat(None).await?;

        // To be scheduled.
        let resync = self.continuous_sync_inputs();
        let new_inputs = self.config.inputs.monitor_results(new_coverage, true);
        let new_crashes = self.config.crashes.monitor_results(new_result, true);

        let (stats_sender, stats_receiver) = mpsc::unbounded_channel();
        let report_stats = report_runtime_stats(stats_receiver, hb_client);
        let fuzzers = self.run_fuzzers(Some(&stats_sender));
        futures::try_join!(resync, new_inputs, new_crashes, fuzzers, report_stats)?;

        Ok(())
    }

    pub async fn verify(&self) -> Result<()> {
        let mut directories = vec![self.config.inputs.local_path.clone()];
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            let mut dirs = readonly_inputs
                .iter()
                .map(|x| x.local_path.clone())
                .collect();
            directories.append(&mut dirs);
        }

        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.common.setup_dir,
        );
        fuzzer
            .verify(self.config.check_fuzzer_help, Some(directories))
            .await
    }

    pub async fn run_fuzzers(&self, stats_sender: Option<&StatsSender>) -> Result<()> {
        let fuzzers: Vec<_> = (0..self.workers())
            .map(|id| self.start_fuzzer_monitor(id, stats_sender))
            .collect();

        try_join_all(fuzzers).await?;

        Ok(())
    }

    /// Creates a temporary directory in the current task directory
    async fn create_local_temp_dir(&self) -> Result<TempDir> {
        let task_dir = self
            .config
            .inputs
            .local_path
            .parent()
            .ok_or_else(|| anyhow!("Invalid input path"))?;
        let temp_path = task_dir.join(".temp");
        tokio::fs::create_dir_all(&temp_path).await?;
        let temp_dir = tempdir_in(temp_path)?;
        Ok(temp_dir)
    }

    // The fuzzer monitor coordinates a _series_ of fuzzer runs.
    //
    // A run is one session of continuous fuzzing, terminated by a fuzzing error
    // or discovered fault. The monitor restarts the libFuzzer when it exits.
    pub async fn start_fuzzer_monitor(
        &self,
        worker_id: usize,
        stats_sender: Option<&StatsSender>,
    ) -> Result<()> {
        let local_input_dir = self.create_local_temp_dir().await?;
        loop {
            let instant = Instant::now();
            self.run_fuzzer(&local_input_dir.path(), worker_id, stats_sender)
                .await?;

            let mut entries = tokio::fs::read_dir(local_input_dir.path()).await?;
            while let Ok(Some(entry)) = entries.next_entry().await {
                let destination_path = self
                    .config
                    .inputs
                    .local_path
                    .clone()
                    .join(entry.file_name());
                tokio::fs::rename(&entry.path(), &destination_path)
                    .await
                    .with_context(|| {
                        format!(
                            "unable to move crashing input into results directory: {} - {}",
                            entry.path().display(),
                            destination_path.display()
                        )
                    })?;
            }

            // if libFuzzer is exiting rapidly, give some breathing room to allow the
            // handles to be reaped.
            let runtime = instant.elapsed();
            if runtime < COOLOFF_PERIOD {
                sleep(COOLOFF_PERIOD - runtime).await;
            }
        }
    }

    // Fuzz with a libFuzzer until it exits.
    //
    // While it runs, parse stderr for progress metrics, and report them.
    async fn run_fuzzer(
        &self,
        local_inputs: impl AsRef<std::path::Path>,
        worker_id: usize,
        stats_sender: Option<&StatsSender>,
    ) -> Result<()> {
        let crash_dir = self.create_local_temp_dir().await?;
        let run_id = Uuid::new_v4();

        debug!("starting fuzzer run, run_id = {}", run_id);

        let mut inputs = vec![&self.config.inputs.local_path];
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            readonly_inputs
                .iter()
                .for_each(|d| inputs.push(&d.local_path));
        }

        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.common.setup_dir,
        );
        let mut running = fuzzer.fuzz(crash_dir.path(), local_inputs, &inputs).await?;
        let running_id = running.id();
        let notify = Arc::new(Notify::new());
        let sys_info = task::spawn(report_fuzzer_sys_info(
            worker_id,
            run_id,
            running_id.unwrap_or_default(),
            notify.clone(),
        ));

        // Splitting borrow.
        let stderr = running
            .stderr
            .as_mut()
            .ok_or_else(|| format_err!("stderr not captured"))?;
        let mut stderr = BufReader::new(stderr);

        let mut libfuzzer_output: ArrayDeque<[_; LOGS_BUFFER_SIZE], Wrapping> = ArrayDeque::new();
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
            libfuzzer_output.push_back(line);
        }

        let exit_status = running.wait().await;
        notify.notify_one();
        let _ = sys_info.await;

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
                    libfuzzer_output
                        .into_iter()
                        .collect::<Vec<String>>()
                        .join("\n")
                );
            } else {
                warn!(
                    "libfuzzer exited without generating crashes, continuing.  status:{} stderr:{:?}",
                    serde_json::to_string(&exit_status)?,
                    libfuzzer_output.into_iter().collect::<Vec<String>>().join("\n")
                );
            }
        }

        for file in &files {
            if let Some(filename) = file.file_name() {
                let dest = self.config.crashes.local_path.join(filename);
                if let Err(e) = tokio::fs::rename(file.clone(), dest.clone()).await {
                    if !dest.exists() {
                        bail!(e)
                    }
                }
            }
        }

        Ok(())
    }

    async fn init_directories(&self) -> Result<()> {
        self.config.inputs.init_pull().await?;
        self.config.crashes.init().await?;
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            for dir in readonly_inputs {
                dir.init_pull().await?;
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
    worker_id: usize,
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

async fn report_fuzzer_sys_info(
    worker_id: usize,
    run_id: Uuid,
    fuzzer_pid: u32,
    cancellation: Arc<Notify>,
) -> Result<()> {
    // Allow for sampling CPU usage.
    let mut period = tokio::time::interval_at(
        Instant::now() + PROC_INFO_COLLECTION_DELAY,
        PROC_INFO_PERIOD,
    );
    loop {
        select! {
            () = cancellation.notified() => break,
            _ = period.tick() => (),
        }

        // process doesn't exist
        if !system::refresh_process(fuzzer_pid)? {
            break;
        }

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
    }

    Ok(())
}

#[derive(Clone, Debug)]
pub struct RuntimeStats {
    worker_id: usize,
    run_id: Uuid,
    count: u64,
    execs_sec: f64,
}

#[derive(Debug, Default)]
pub struct TotalStats {
    worker_stats: HashMap<usize, RuntimeStats>,
    count: u64,
    execs_sec: f64,
}

impl TotalStats {
    fn update(&mut self, worker_data: RuntimeStats) {
        if let Some(current) = self.worker_stats.get(&worker_data.worker_id) {
            // if it's the same run, only add the differences
            if current.run_id == worker_data.run_id {
                self.count += worker_data.count.saturating_sub(current.count);
            } else {
                self.count += worker_data.count;
            }
        } else {
            self.count += worker_data.count;
        }

        self.worker_stats.insert(worker_data.worker_id, worker_data);

        self.execs_sec = self.worker_stats.values().map(|x| x.execs_sec).sum();
    }

    fn report(&self) {
        event!(
            runtime_stats;
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
        sleep(self.interval).await;
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
    mut stats_channel: mpsc::UnboundedReceiver<RuntimeStats>,
    heartbeat_client: impl HeartbeatSender,
) -> Result<()> {
    // Cache the last-reported stats for a given worker.
    //
    // When logging stats, the most recently reported runtime stats will be used for any
    // missing data. For time-triggered logging, it will be used for all workers.
    let mut total = TotalStats::default();

    // report all zeros to start
    total.report();

    let timer = Timer::new(RUNTIME_STATS_PERIOD);

    loop {
        tokio::select! {
            Some(stats) = stats_channel.recv() => {
                heartbeat_client.alive();
                total.update(stats);
                total.report()
            }
            _ = timer.wait() => {
                total.report()
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{RuntimeStats, TotalStats};
    use anyhow::Result;
    use uuid::Uuid;

    #[test]
    fn test_total_stats() -> Result<()> {
        let mut total = TotalStats::default();
        let mut a = RuntimeStats {
            worker_id: 0,
            run_id: Uuid::new_v4(),
            count: 0,
            execs_sec: 0.0,
        };

        total.update(a.clone());
        assert!(total.execs_sec == 0.0);
        assert!(total.count == 0);

        // same run of existing worker, but counts & execs_sec increased.
        a.count += 10;
        a.execs_sec = 1.0;
        total.update(a.clone());
        assert!(total.execs_sec == 1.0);
        assert!(total.count == 10);

        // same run of existing worker.  counts and execs should stay the same.
        total.update(a.clone());
        assert!(total.count == 10);
        assert!(total.execs_sec == 1.0);

        // new run of existing worker. counts should go up, execs_sec should
        // equal new worker value.
        a.run_id = Uuid::new_v4();
        a.execs_sec = 2.0;
        total.update(a.clone());
        assert!(total.count == 20);
        assert!(total.execs_sec == 2.0);

        // existing worker, now new data.  totals should stay the same.
        total.update(a.clone());
        assert!(total.count == 20);
        assert!(total.execs_sec == 2.0);

        // new worker, counts & execs_sec should go up by data from worker.
        let mut b = RuntimeStats {
            worker_id: 1,
            run_id: Uuid::new_v4(),
            count: 10,
            execs_sec: 2.0,
        };
        total.update(b.clone());
        assert!(total.count == 30);
        assert!(total.execs_sec == 4.0);

        // existing run for existing worker.
        // count should go up by 1, execs_sec down by 1.
        b.count += 1;
        b.execs_sec -= 1.0;
        total.update(b.clone());
        assert!(total.count == 31);
        assert!(total.execs_sec == 3.0);

        Ok(())
    }
}
