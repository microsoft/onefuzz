// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! # Coverage Task
//!
//! Computes a streaming coverage metric using Sancov-instrumented libFuzzers.
//! Reports the latest coverage rate via telemetry events and updates a remote
//! total coverage map in blob storage.
//!
//! ## Instrumentation
//!
//! Assumes the libFuzzer is instrumented with Sancov inline 8-bit counters.
//! This feature updates a global table without any PC callback. The coverage
//! scripts find and dump this table after executing the test input. For now,
//! our metric projects the counter value to a single bit, treating each table
//! entry as a flag rather than a counter.
//!
//! ## Dependencies
//!
//! This task invokes OS-specific debugger scripts to dump the coverage for
//! each input. To do this, the  following must be in the `$PATH`:
//!
//! ### Linux
//! - `python3` (3.6)
//! - `gdb` (8.1)
//!
//! ### Windows
//! - `powershell.exe` (5.1)
//! - `cdb.exe` (10.0)
//!
//! Versions in parentheses have been tested.

use crate::tasks::config::SyncedDir;
use crate::tasks::coverage::{recorder::CoverageRecorder, total::TotalCoverage};
use crate::tasks::heartbeat::*;
use crate::tasks::utils::{init_dir, sync_remote_dir, SyncOperation};
use crate::tasks::{config::CommonConfig, generic::input_poller::*};
use anyhow::Result;
use async_trait::async_trait;
use futures::stream::StreamExt;
use onefuzz::fs::list_files;
use onefuzz::telemetry::Event::coverage_data;
use onefuzz::telemetry::EventData;
use reqwest::Url;
use serde::Deserialize;
use std::collections::HashMap;
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
    sync::Arc,
};
use storage_queue::Message;
use tokio::fs;

const TOTAL_COVERAGE: &str = "total.cov";

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub input_queue: Option<Url>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

/// Compute the coverage provided by one or both of:
///
///     1. A list of seed corpus containers (one-time batch mode)
///     2. A queue of inputs pending coverage analysis (streaming)
///
/// If `seed_containers` is empty and `input_queue` is absent, this task
/// will do nothing. If `input_queue` is present, then this task will poll
/// forever.
pub struct CoverageTask {
    config: Arc<Config>,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: impl Into<Arc<Config>>) -> Self {
        let config = config.into();

        let task_dir = PathBuf::from(config.common.task_id.to_string());
        let poller_dir = task_dir.join("poller");
        let poller = InputPoller::<Message>::new(poller_dir);

        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting libFuzzer coverage task");

        init_dir(&self.config.coverage.path).await?;
        verbose!(
            "initialized coverage dir, path = {}",
            self.config.coverage.path.display()
        );

        sync_remote_dir(&self.config.coverage, SyncOperation::Pull).await?;
        verbose!(
            "synced coverage dir, path = {}",
            self.config.coverage.path.display()
        );

        self.process().await
    }

    async fn process(&mut self) -> Result<()> {
        let mut processor = CoverageProcessor::new(self.config.clone()).await?;

        // Update the total with the coverage from each seed corpus.
        for dir in &self.config.readonly_inputs {
            verbose!("recording coverage for {}", dir.path.display());
            init_dir(&dir.path).await?;
            sync_remote_dir(&dir, SyncOperation::Pull).await?;
            self.record_corpus_coverage(&mut processor, dir).await?;
            fs::remove_dir_all(&dir.path).await?;
            sync_remote_dir(&self.config.coverage, SyncOperation::Push).await?;
        }

        info!(
            "recorded coverage for {} containers in `readonly_inputs`",
            self.config.readonly_inputs.len(),
        );

        // If a queue has been provided, poll it for new coverage.
        if let Some(queue) = &self.config.input_queue {
            verbose!("polling queue for new coverage");
            let callback = CallbackImpl::new(queue.clone(), processor);
            self.poller.run(callback).await?;
        }

        Ok(())
    }

    async fn record_corpus_coverage(
        &self,
        processor: &mut CoverageProcessor,
        corpus_dir: &SyncedDir,
    ) -> Result<()> {
        let mut corpus = fs::read_dir(&corpus_dir.path).await?;

        while let Some(input) = corpus.next().await {
            let input = match input {
                Ok(input) => input,
                Err(err) => {
                    error!("{}", err);
                    continue;
                }
            };

            processor.test_input(&input.path()).await?;
        }

        Ok(())
    }
}

pub struct CoverageProcessor {
    config: Arc<Config>,
    pub recorder: CoverageRecorder,
    pub total: TotalCoverage,
    pub module_totals: HashMap<OsString, TotalCoverage>,
    heartbeat_client: Option<TaskHeartbeatClient>,
}

impl CoverageProcessor {
    pub async fn new(config: Arc<Config>) -> Result<Self> {
        let heartbeat_client = config.common.init_heartbeat().await?;
        let total = TotalCoverage::new(config.coverage.path.join(TOTAL_COVERAGE));
        let recorder = CoverageRecorder::new(config.clone());
        let module_totals = HashMap::default();

        Ok(Self {
            config,
            recorder,
            total,
            module_totals,
            heartbeat_client,
        })
    }

    async fn update_module_total(&mut self, file: &Path, data: &[u8]) -> Result<()> {
        let module = file
            .file_name()
            .ok_or_else(|| format_err!("module must have filename"))?
            .to_os_string();

        verbose!("updating module info {:?}", module);

        if !self.module_totals.contains_key(&module) {
            let parent = &self.config.coverage.path.join("by-module");
            fs::create_dir_all(parent).await?;
            let module_total = parent.join(&module);
            let total = TotalCoverage::new(module_total);
            self.module_totals.insert(module.clone(), total);
        }

        self.module_totals[&module].update_bytes(data).await?;

        verbose!("updated {:?}", module);
        Ok(())
    }

    async fn collect_by_module(&mut self, path: &Path) -> Result<PathBuf> {
        let files = list_files(&path).await?;
        let mut sum = Vec::new();

        for file in &files {
            verbose!("checking {:?}", file);
            let mut content = fs::read(file).await?;
            self.update_module_total(file, &content).await?;
            sum.append(&mut content);
        }

        let mut combined = path.as_os_str().to_owned();
        combined.push(".cov");

        fs::write(&combined, sum).await?;

        Ok(combined.into())
    }

    pub async fn test_input(&mut self, input: &Path) -> Result<()> {
        info!("processing input {:?}", input);
        let new_coverage = self.recorder.record(input).await?;
        let combined = self.collect_by_module(&new_coverage).await?;
        self.total.update(&combined).await?;
        Ok(())
    }

    pub async fn report_total(&self) -> Result<()> {
        let info = self.total.info().await?;
        event!(coverage_data; EventData::Covered = info.covered, EventData::Features = info.features, EventData::Rate = info.rate);
        Ok(())
    }
}

#[async_trait]
impl Processor for CoverageProcessor {
    async fn process(&mut self, _url: Url, input: &Path) -> Result<()> {
        self.heartbeat_client.alive();
        self.test_input(input).await?;
        self.report_total().await?;
        sync_remote_dir(&self.config.coverage, SyncOperation::Push).await?;
        Ok(())
    }
}
