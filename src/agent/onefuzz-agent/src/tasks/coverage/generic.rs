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
use url::Url;

use crate::tasks::config::CommonConfig;
use crate::tasks::generic::input_poller::{CallbackImpl, InputPoller, Processor};
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

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

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
        self.config.coverage.init_pull().await?;

        let cache = self.load_module_cache().await?;
        let mut context = TaskContext::new(cache, &self.config);

        for dir in &self.config.readonly_inputs {
            dir.init_pull().await?;
            context.on_corpus(&dir.path).await?;
        }

        if let Some(queue) = &self.config.input_queue {
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
    cache: ModuleCache,
    config: &'a Config,
    coverage: CommandBlockCov,
}

impl<'a> TaskContext<'a> {
    pub fn new(cache: ModuleCache, config: &'a Config) -> Self {
        // TODO: load existing
        let coverage = CommandBlockCov::default();

        Self { cache, config, coverage }
    }

    pub async fn on_input(&mut self, input: &Path) -> Result<()> {
        use coverage::block::linux::Recorder;

        let mut recorder = Recorder::new(&mut self.cache, CmdFilter::default());
        let cmd = command_for_input(self.config, input)?;

        // TODO: spawn_blocking
        recorder.record(cmd)?;

        self.coverage.merge_max(recorder.coverage());
        Ok(())
    }

    pub async fn on_corpus(&mut self, dir: &Path) -> Result<()> {
        use futures::stream::StreamExt;

        let mut corpus = fs::read_dir(dir)
            .await
            .with_context(|| format!("unable to read corpus directory: {}", dir.display()))?;

        while let Some(entry) = corpus.next().await {
            match entry {
                Ok(entry) => {
                    if entry.file_type().await?.is_file() {
                        self.on_input(&entry.path()).await?;
                    } else {
                        warn!("skipping non-file dir entry: {}", entry.path().display());
                    }
                },
                Err(err) => {
                    error!("{:?}", err);
                },
            }
        }

        Ok(())
    }
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.on_input(input).await?;

        Ok(())
    }
}

fn command_for_input(config: &Config, input: impl AsRef<Path>) -> Result<Command> {
    let expand = Expand::new()
        .input_path(input)
        .job_id(&config.common.job_id)
        .setup_dir(&config.common.setup_dir)
        .target_options(&config.target_options)
        .task_id(&config.common.task_id);

    let mut cmd = Command::new(&config.target_exe);

    let target_options = expand.evaluate(&config.target_options)?;
    cmd.args(target_options);

    for (k, v) in &config.target_env {
        cmd.env(k, expand.evaluate_value(v)?);
    }

    cmd.env_remove("RUST_LOG");
    cmd.stdin(Stdio::null());
    cmd.stdout(Stdio::piped());
    cmd.stderr(Stdio::piped());

    Ok(cmd)
}