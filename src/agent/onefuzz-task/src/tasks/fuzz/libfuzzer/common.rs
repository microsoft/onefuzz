// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::default_bool_true,
};
use anyhow::{Context, Result};
use arraydeque::{ArrayDeque, Wrapping};
use async_trait::async_trait;
use futures::future::try_join_all;
use onefuzz::{
    fs::list_files,
    libfuzzer::{LibFuzzer, LibFuzzerLine},
    process::ExitStatus,
    syncdir::{continuous_sync, SyncOperation::Pull, SyncedDir},
};
use onefuzz_result::job_result::{JobResultData, JobResultSender, TaskJobResultClient};
use onefuzz_telemetry::{
    Event::{new_coverage, new_crashdump, new_result, runtime_stats},
    EventData,
};
use serde::Deserialize;
use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    fmt::Debug,
    path::{Path, PathBuf},
    sync::Arc,
};
use tempfile::{tempdir_in, TempDir};
use tokio::{
    io::{AsyncBufReadExt, BufReader},
    sync::{mpsc, Notify},
    time::{sleep, Duration, Instant},
};
use uuid::Uuid;

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

/// LibFuzzer subtypes that share custom configuration or process initialization.
#[async_trait]
pub trait LibFuzzerType: Send + Sync {
    /// Extra configuration values expected by the `Config` for this type.
    type Config: Send + Sync;

    /// Method that constructs a `LibFuzzer` configured as appropriate for the subtype.
    ///
    /// This may include things like setting special environment variables, or overriding
    /// the defaults or values of some command arguments.
    async fn from_config(config: &Config<Self>) -> Result<LibFuzzer>;

    /// Perform any environmental setup common to all targets of this fuzzer type.
    ///
    /// Defaults to a no-op.
    ///
    /// Executed after initializating remote-backed corpora.
    async fn extra_setup(_config: &Config<Self>) -> Result<()> {
        Ok(())
    }
}

#[derive(Debug, Deserialize, Clone)]
pub struct Config<L: LibFuzzerType + Send + Sync + ?Sized> {
    pub inputs: SyncedDir,
    pub readonly_inputs: Option<Vec<SyncedDir>>,
    pub crashes: SyncedDir,
    pub crashdumps: Option<SyncedDir>,
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

    #[serde(flatten)]
    pub extra: L::Config,
}

pub struct LibFuzzerFuzzTask<L>
where
    L: LibFuzzerType,
    Config<L>: Debug,
{
    config: Config<L>,
}

impl<L> LibFuzzerFuzzTask<L>
where
    L: LibFuzzerType,
    Config<L>: Debug,
{
    pub fn new(config: Config<L>) -> Result<Self> {
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
        L::extra_setup(&self.config).await?;
        self.verify().await?;

        let hb_client = self.config.common.init_heartbeat(None).await?;
        let jr_client = self.config.common.init_job_result().await?;

        // To be scheduled.
        let resync = self.continuous_sync_inputs();

        let new_inputs = self
            .config
            .inputs
            .monitor_results(new_coverage, true, &jr_client);
        let new_crashes = self
            .config
            .crashes
            .monitor_results(new_result, true, &jr_client);
        let new_crashdumps = async {
            if let Some(crashdumps) = &self.config.crashdumps {
                crashdumps
                    .monitor_results(new_crashdump, true, &jr_client)
                    .await
            } else {
                Ok(())
            }
        };

        let (stats_sender, stats_receiver) = mpsc::unbounded_channel();
        let report_stats = report_runtime_stats(stats_receiver, &hb_client, &jr_client);
        let fuzzers = self.run_fuzzers(Some(&stats_sender));
        futures::try_join!(
            resync,
            new_inputs,
            new_crashes,
            new_crashdumps,
            fuzzers,
            report_stats
        )?;

        Ok(())
    }

    pub async fn verify(&self) -> Result<()> {
        let mut directories: Vec<&Path> = vec![&self.config.inputs.local_path];
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            directories.extend(readonly_inputs.iter().map(|x| -> &Path { &x.local_path }));
        }

        let fuzzer = L::from_config(&self.config).await?;
        fuzzer
            .verify(self.config.check_fuzzer_help, Some(&directories))
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
            .ok_or_else(|| anyhow!("invalid input path"))?;
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
        local_inputs: impl AsRef<Path>,
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

        info!("config is: {:?}", self.config);

        let fuzzer = L::from_config(&self.config).await?;
        let mut running = fuzzer.fuzz(crash_dir.path(), local_inputs, &inputs).await?;

        info!("child is: {:?}", running);

        #[cfg(target_os = "linux")]
        let pid = running.id();

        let notify = Arc::new(Notify::new());

        // Splitting borrow.
        let stderr = running
            .stderr
            .as_mut()
            .ok_or_else(|| format_err!("stderr not captured"))?;
        let mut stderr = BufReader::new(stderr);

        let mut libfuzzer_output: ArrayDeque<_, LOGS_BUFFER_SIZE, Wrapping> = ArrayDeque::new();
        {
            let mut buf = vec![];
            loop {
                buf.clear();
                let bytes_read = stderr.read_until(b'\n', &mut buf).await?;
                if bytes_read == 0 && buf.is_empty() {
                    break;
                }
                let line = String::from_utf8_lossy(&buf).to_string();
                if let Some(stats_sender) = stats_sender {
                    if let Err(err) = try_report_iter_update(stats_sender, worker_id, run_id, &line)
                    {
                        error!("could not parse fuzzing interation update: {}", err);
                    }
                }
                libfuzzer_output.push_back(line);
            }
        }

        let exit_status = running.wait().await;
        notify.notify_one();

        let exit_status: ExitStatus = exit_status?.into();

        info!(
            "fuzzer exited, here are the last {} lines of stderr:",
            libfuzzer_output.len()
        );
        info!("------------------------");
        for line in libfuzzer_output.iter() {
            info!("{}", line.trim_end_matches('\n'));
        }
        info!("------------------------");

        let files = list_files(crash_dir.path()).await?;

        info!("found {} crashes", files.len());

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

        // name the dumpfile after the crash file (if one)
        // otherwise don't rename it
        let dump_file_name: Option<OsString> = if files.len() == 1 {
            files
                .first()
                .and_then(|f| f.file_name().map(OsStr::to_os_string))
        } else {
            None
        };

        // move crashing inputs to output directory
        for file in files {
            if let Some(filename) = file.file_name() {
                let dest = self.config.crashes.local_path.join(filename);
                if let Err(e) = tokio::fs::rename(file, dest.clone()).await {
                    if !dest.exists() {
                        bail!(e)
                    }
                }
            }
        }

        if let Some(crashdumps) = &self.config.crashdumps {
            // check for core dumps on Linux:
            // note that collecting the dumps must be enabled by the template
            #[cfg(target_os = "linux")]
            if let Some(pid) = pid {
                // expect crash dump to exist in CWD
                let filename = format!("core.{pid}");
                let dest_filename = dump_file_name.as_deref().unwrap_or(OsStr::new(&filename));
                let dest_path = crashdumps.local_path.join(dest_filename);
                match tokio::fs::rename(&filename, &dest_path).await {
                    Ok(()) => {
                        info!(
                            "moved crash dump {} to output directory: {}",
                            filename,
                            dest_path.display()
                        );
                    }
                    Err(e) => {
                        if e.kind() == std::io::ErrorKind::NotFound {
                            // okay, no crash dump found
                            info!("no crash dump found with name: {}", filename);
                        } else {
                            return Err(e).context("moving crash dump to output directory");
                        }
                    }
                }
            } else {
                warn!("no PID found for libfuzzer process");
            }

            // check for crash dumps on Windows:
            #[cfg(target_os = "windows")]
            {
                let dumpfile_extension = Some(std::ffi::OsStr::new("dmp"));

                let mut working_dir = tokio::fs::read_dir(".").await?;
                let mut found_dump = false;
                while let Some(next) = working_dir.next_entry().await? {
                    if next.path().extension() == dumpfile_extension {
                        // Windows dumps get a fixed filename so we will generate a random one,
                        // if there's no valid target crash name:
                        let dest_filename = dump_file_name
                            .unwrap_or_else(|| uuid::Uuid::new_v4().to_string().into());
                        let dest_path = crashdumps.local_path.join(&dest_filename);
                        tokio::fs::rename(next.path(), &dest_path)
                            .await
                            .context("moving crash dump to output directory")?;
                        info!(
                            "moved crash dump {} to output directory: {}",
                            next.path().display(),
                            dest_path.display()
                        );
                        found_dump = true;
                        break;
                    }
                }

                if !found_dump {
                    info!("no crash dump found with extension .dmp");
                }
            }
        }

        Ok(())
    }

    async fn init_directories(&self) -> Result<()> {
        // input directories (init_pull):
        self.config.inputs.init_pull().await?;
        if let Some(readonly_inputs) = &self.config.readonly_inputs {
            for dir in readonly_inputs {
                dir.init_pull().await?;
            }
        }

        // output directories (init):
        self.config.crashes.init().await?;
        if let Some(crashdumps) = &self.config.crashdumps {
            crashdumps.init().await?;
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

    async fn report(&self, jr_client: &Option<TaskJobResultClient>) {
        event!(
            runtime_stats;
            EventData::Count = self.count,
            EventData::ExecsSecond = self.execs_sec
        );
        metric!(
            runtime_stats;
            1.0;
            EventData::Count = self.count,
            EventData::ExecsSecond = self.execs_sec
        );
        if let Some(jr_client) = jr_client {
            let _ = jr_client
                .send_direct(
                    JobResultData::RuntimeStats,
                    HashMap::from([
                        ("total_count".to_string(), self.count as f64),
                        ("execs_sec".to_string(), self.execs_sec),
                    ]),
                )
                .await;
        }
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
    heartbeat_client: &Option<TaskHeartbeatClient>,
    jr_client: &Option<TaskJobResultClient>,
) -> Result<()> {
    // Cache the last-reported stats for a given worker.
    //
    // When logging stats, the most recently reported runtime stats will be used for any
    // missing data. For time-triggered logging, it will be used for all workers.
    let mut total = TotalStats::default();

    // report all zeros to start
    total.report(jr_client).await;

    let timer = Timer::new(RUNTIME_STATS_PERIOD);

    loop {
        tokio::select! {
            Some(stats) = stats_channel.recv() => {
                heartbeat_client.alive();
                total.update(stats);
                total.report(jr_client).await
            }
            _ = timer.wait() => {
                total.report(jr_client).await
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
        total.update(a);
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
        total.update(b);
        assert!(total.count == 31);
        assert!(total.execs_sec == 3.0);

        Ok(())
    }
}
