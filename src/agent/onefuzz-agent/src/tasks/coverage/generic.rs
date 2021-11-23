// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{Context, Result};
use async_trait::async_trait;
use coverage::block::CommandBlockCov;
use coverage::cache::ModuleCache;
use coverage::code::{CmdFilter, CmdFilterDef};
use onefuzz::expand::{Expand, PlaceHolder};
use onefuzz::syncdir::SyncedDir;
use onefuzz_telemetry::{warn, Event::coverage_data, EventData};
use serde::de::DeserializeOwned;
use storage_queue::{Message, QueueClient};
use tokio::fs;
use tokio::task::spawn_blocking;
use tokio_stream::wrappers::ReadDirStream;
use url::Url;

use crate::tasks::config::CommonConfig;
use crate::tasks::generic::input_poller::{CallbackImpl, InputPoller, Processor};
use crate::tasks::heartbeat::{HeartbeatSender, TaskHeartbeatClient};

const MAX_COVERAGE_RECORDING_ATTEMPTS: usize = 2;
const COVERAGE_FILE: &str = "coverage.json";
const MODULE_CACHE_FILE: &str = "module-cache.json";

const DEFAULT_TARGET_TIMEOUT: Duration = Duration::from_secs(5);

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,

    pub coverage_filter: Option<String>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

impl Config {
    pub fn timeout(&self) -> Duration {
        self.target_timeout
            .map(Duration::from_secs)
            .unwrap_or(DEFAULT_TARGET_TIMEOUT)
    }
}

pub struct CoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting coverage task");

        self.config.coverage.init_pull().await?;

        let cache = deserialize_or_default(MODULE_CACHE_FILE).await?;

        let coverage_file = self.config.coverage.local_path.join(COVERAGE_FILE);
        let coverage = deserialize_or_default(coverage_file).await?;

        let filter = self.load_filter().await?;
        let heartbeat = self.config.common.init_heartbeat(None).await?;
        let mut context = TaskContext::new(cache, &self.config, coverage, filter, heartbeat);

        if !context.uses_input() {
            bail!("input is not specified on the command line or arguments for the target");
        }

        context.heartbeat.alive();

        let mut seen_inputs = false;

        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.local_path.display());

            dir.init_pull().await?;
            let dir_count = context.record_corpus(&dir.local_path).await?;

            if dir_count > 0 {
                seen_inputs = true;
            }

            info!(
                "recorded coverage for {} inputs from {}",
                dir_count,
                dir.local_path.display()
            );

            context.heartbeat.alive();
        }

        if seen_inputs {
            context.report_coverage_stats().await?;
            context.save_and_sync_coverage().await?;
        }

        context.heartbeat.alive();

        if let Some(queue) = &self.config.input_queue {
            info!("polling queue for new coverage inputs");

            let callback = CallbackImpl::new(queue.clone(), context)?;
            self.poller.run(callback).await?;
        }

        Ok(())
    }

    async fn load_filter(&self) -> Result<CmdFilter> {
        let raw_filter_path = if let Some(raw_path) = &self.config.coverage_filter {
            raw_path
        } else {
            return Ok(CmdFilter::default());
        };

        // Ensure users can locate the filter relative to the setup container.
        let expand = Expand::new().setup_dir(&self.config.common.setup_dir);
        let filter_path = expand.evaluate_value(raw_filter_path)?;

        let data = fs::read(&filter_path).await?;
        let def: CmdFilterDef = serde_json::from_slice(&data)?;
        let filter = CmdFilter::new(def)?;

        Ok(filter)
    }
}

async fn deserialize_or_default<T>(path: impl AsRef<Path>) -> Result<T>
where
    T: Default + DeserializeOwned,
{
    use tokio::io::ErrorKind::NotFound;

    let data = fs::read(path).await;

    if let Err(err) = &data {
        if err.kind() == NotFound {
            return Ok(T::default());
        }
    }

    let data = data?;

    Ok(serde_json::from_slice(&data)?)
}

struct TaskContext<'a> {
    cache: Arc<Mutex<ModuleCache>>,
    config: &'a Config,
    coverage: CommandBlockCov,
    filter: CmdFilter,
    heartbeat: Option<TaskHeartbeatClient>,
}

impl<'a> TaskContext<'a> {
    pub fn new(
        cache: ModuleCache,
        config: &'a Config,
        coverage: CommandBlockCov,
        filter: CmdFilter,
        heartbeat: Option<TaskHeartbeatClient>,
    ) -> Self {
        let cache = Arc::new(Mutex::new(cache));

        Self {
            cache,
            config,
            coverage,
            filter,
            heartbeat,
        }
    }

    pub async fn record_input(&mut self, input: &Path) -> Result<()> {
        debug!("recording coverage for {}", input.display());
        let attempts = MAX_COVERAGE_RECORDING_ATTEMPTS;

        for attempt in 1..=attempts {
            let result = self.try_record_input(input).await;

            if let Err(err) = &result {
                // Recording failed, check if we can retry.
                if attempt < attempts {
                    // We will retry, but warn to capture the error if we succeed.
                    warn!(
                        "error recording coverage for input = {}: {:?}",
                        input.display(),
                        err
                    );
                } else {
                    // Final attempt, do not retry.
                    return result.with_context(|| {
                        format_err!(
                            "failed to record coverage for input = {} after {} attempts",
                            input.display(),
                            attempts
                        )
                    });
                }
            } else {
                // We successfully recorded the coverage for `input`, so stop.
                break;
            }
        }

        Ok(())
    }

    async fn try_record_input(&mut self, input: &Path) -> Result<()> {
        let coverage = self.record_impl(input).await?;
        self.coverage.merge_max(&coverage);

        Ok(())
    }

    async fn record_impl(&mut self, input: &Path) -> Result<CommandBlockCov> {
        let cache = Arc::clone(&self.cache);
        let filter = self.filter.clone();
        let cmd = self.command_for_input(input).await?;
        let timeout = self.config.timeout();
        let coverage = spawn_blocking(move || {
            let mut cache = cache
                .lock()
                .map_err(|_| format_err!("module cache mutex lock was poisoned"))?;
            record_os_impl(cmd, timeout, &mut cache, filter)
        })
        .await??;

        Ok(coverage)
    }

    fn uses_input(&self) -> bool {
        let input = PlaceHolder::Input.get_string();

        for entry in &self.config.target_options {
            if entry.contains(&input) {
                return true;
            }
        }
        for (k, v) in &self.config.target_env {
            if k == &input || v.contains(&input) {
                return true;
            }
        }

        false
    }

    async fn command_for_input(&self, input: &Path) -> Result<Command> {
        let expand = Expand::new()
            .machine_id()
            .await?
            .input_path(input)
            .job_id(&self.config.common.job_id)
            .setup_dir(&self.config.common.setup_dir)
            .target_exe(&self.config.target_exe)
            .target_options(&self.config.target_options)
            .task_id(&self.config.common.task_id);

        let mut cmd = Command::new(&self.config.target_exe);

        let target_options = expand.evaluate(&self.config.target_options)?;
        cmd.args(target_options);

        for (k, v) in &self.config.target_env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        cmd.env_remove("RUST_LOG");
        cmd.stdin(Stdio::null());
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        Ok(cmd)
    }

    pub async fn record_corpus(&mut self, dir: &Path) -> Result<usize> {
        use futures::stream::StreamExt;

        let mut corpus = fs::read_dir(dir)
            .await
            .map(ReadDirStream::new)
            .with_context(|| format!("unable to read corpus directory: {}", dir.display()))?;

        let mut count = 0;

        while let Some(entry) = corpus.next().await {
            match entry {
                Ok(entry) => {
                    if entry.file_type().await?.is_file() {
                        self.record_input(&entry.path()).await?;
                        count += 1;

                        // make sure we save & sync coverage every 10 inputs
                        if count % 10 == 0 {
                            self.save_and_sync_coverage().await?;
                        }
                    } else {
                        warn!("skipping non-file dir entry: {}", entry.path().display());
                    }
                }
                Err(err) => {
                    error!("{:?}", err);
                }
            }
        }

        Ok(count)
    }

    pub async fn report_coverage_stats(&self) -> Result<()> {
        use EventData::*;

        let s = CoverageStats::new(&self.coverage);
        event!(coverage_data; Covered = s.covered, Features = s.features, Rate = s.rate);

        Ok(())
    }

    pub async fn save_and_sync_coverage(&self) -> Result<()> {
        let path = self.config.coverage.local_path.join(COVERAGE_FILE);
        let text = serde_json::to_string(&self.coverage).context("serializing coverage to JSON")?;

        fs::write(&path, &text)
            .await
            .with_context(|| format!("writing coverage to {}", path.display()))?;
        self.config.coverage.sync_push().await?;

        Ok(())
    }
}

#[cfg(target_os = "linux")]
fn record_os_impl(
    cmd: Command,
    timeout: Duration,
    cache: &mut ModuleCache,
    filter: CmdFilter,
) -> Result<CommandBlockCov> {
    use coverage::block::linux::Recorder;

    let coverage = Recorder::record(cmd, timeout, cache, filter)?;

    Ok(coverage)
}

#[cfg(target_os = "windows")]
fn record_os_impl(
    cmd: Command,
    timeout: Duration,
    cache: &mut ModuleCache,
    filter: CmdFilter,
) -> Result<CommandBlockCov> {
    use coverage::block::windows::{Recorder, RecorderEventHandler};

    let mut recorder = Recorder::new(cache, filter);
    let mut handler = RecorderEventHandler::new(&mut recorder, timeout);
    handler.run(cmd)?;
    let coverage = recorder.into_coverage();

    Ok(coverage)
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat.alive();

        self.record_input(input).await?;
        self.report_coverage_stats().await?;
        self.save_and_sync_coverage().await?;

        Ok(())
    }
}

#[derive(Default)]
struct CoverageStats {
    covered: u64,
    features: u64,
    rate: f64,
}

impl CoverageStats {
    pub fn new(coverage: &CommandBlockCov) -> Self {
        let mut stats = CoverageStats::default();

        for (_, module) in coverage.iter() {
            for block in module.blocks.values() {
                stats.features += 1;

                if block.count > 0 {
                    stats.covered += 1;
                }
            }
        }

        if stats.features > 0 {
            stats.rate = (stats.covered as f64) / (stats.features as f64)
        }

        stats
    }
}
