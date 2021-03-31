// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

use anyhow::{Context, Result};
use async_trait::async_trait;
use coverage::block::CommandBlockCov;
use coverage::cache::ModuleCache;
use coverage::code::CmdFilter;
use onefuzz::expand::Expand;
use onefuzz::syncdir::SyncedDir;
use storage_queue::{Message, QueueClient};
use tokio::fs;
use tokio::task::spawn_blocking;
use url::Url;

use crate::tasks::config::CommonConfig;
use crate::tasks::generic::input_poller::{CallbackImpl, InputPoller, Processor};
use crate::tasks::heartbeat::{HeartbeatSender, TaskHeartbeatClient};
use crate::tasks::utils::default_bool_true;

const MODULE_CACHE_FILE: &'static str = "module-cache.json";

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(default = "default_bool_true")]
    pub check_queue: bool,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct CoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("generic-coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting generic-coverage task");

        self.config.coverage.init_pull().await?;

        let cache = self.load_module_cache().await?;
        let heartbeat = self.config.common.init_heartbeat().await?;
        let mut context = TaskContext::new(cache, &self.config, heartbeat);

        context.heartbeat.alive();

        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.path.display());

            dir.init_pull().await?;
            let dir_count = context.record_corpus(&dir.path).await?;

            info!(
                "recorded coverage for {} inputs from {}",
                dir_count,
                dir.path.display()
            );

            context.heartbeat.alive();
        }

        context.heartbeat.alive();

        if let Some(queue) = &self.config.input_queue {
            info!("polling queue for new coverage inputs");

            let callback = CallbackImpl::new(queue.clone(), context)?;
            self.poller.run(callback).await?;
        }

        Ok(())
    }

    /// Try to load an existing module cache from disk. If one is not found,
    /// create a new, empty cache.
    async fn load_module_cache(&mut self) -> Result<ModuleCache> {
        let data = fs::read(MODULE_CACHE_FILE).await;

        let cache = if let Ok(data) = &data {
            serde_json::from_slice(data)?
        } else {
            ModuleCache::default()
        };

        Ok(cache)
    }
}

struct TaskContext<'a> {
    // Optional only to enable temporary move into blocking thread.
    cache: Option<ModuleCache>,

    config: &'a Config,
    coverage: CommandBlockCov,
    heartbeat: Option<TaskHeartbeatClient>,
}

impl<'a> TaskContext<'a> {
    pub fn new(
        cache: ModuleCache,
        config: &'a Config,
        heartbeat: Option<TaskHeartbeatClient>,
    ) -> Self {
        let cache = Some(cache);

        // TODO: load existing
        let coverage = CommandBlockCov::default();

        Self {
            cache,
            config,
            coverage,
            heartbeat,
        }
    }

    pub async fn record_input(&mut self, input: &Path) -> Result<()> {
        let coverage = self.record_impl(input).await?;
        self.coverage.merge_max(&coverage);

        Ok(())
    }

    async fn record_impl(&mut self, input: &Path) -> Result<CommandBlockCov> {
        // Invariant: `self.cache` must be present on method enter and exit.
        let cache = self.cache.take().expect("module cache not present");

        let cmd = self.command_for_input(input)?;
        let recorded = spawn_blocking(move || record_os_impl(cache, cmd)).await??;

        // Maintain invariant.
        self.cache = Some(recorded.cache);

        Ok(recorded.coverage)
    }

    fn command_for_input(&self, input: &Path) -> Result<Command> {
        let expand = Expand::new()
            .input_path(input)
            .job_id(&self.config.common.job_id)
            .setup_dir(&self.config.common.setup_dir)
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
            .with_context(|| format!("unable to read corpus directory: {}", dir.display()))?;

        let mut count = 0;

        while let Some(entry) = corpus.next().await {
            match entry {
                Ok(entry) => {
                    if entry.file_type().await?.is_file() {
                        self.record_input(&entry.path()).await?;
                        count += 1;
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
}

struct Recorded {
    pub cache: ModuleCache,
    pub coverage: CommandBlockCov,
}

#[cfg(target_os = "linux")]
fn record_os_impl(mut cache: ModuleCache, cmd: Command) -> Result<Recorded> {
    use coverage::block::linux::Recorder;

    let mut recorder = Recorder::new(&mut cache, CmdFilter::default());
    recorder.record(cmd)?;
    let coverage = recorder.into_coverage();

    Ok(Recorded { cache, coverage })
}

#[cfg(target_os = "windows")]
fn record_os_impl(mut cache: ModuleCache, cmd: Command) -> Result<Recorded> {
    use coverage::block::windows::{Recorder, RecorderEventHandler};

    let mut recorder = Recorder::new(&mut cache, CmdFilter::default());
    let timeout = std::time::Duration::from_secs(5);
    let mut handler = RecorderEventHandler::new(&mut recorder, timeout);
    handler.run(cmd)?;
    let coverage = recorder.into_coverage();

    Ok(Recorded { cache, coverage })
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat.alive();

        self.record_input(input).await?;

        Ok(())
    }
}
