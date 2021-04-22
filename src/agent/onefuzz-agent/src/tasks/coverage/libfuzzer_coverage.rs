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

use crate::tasks::heartbeat::*;
use crate::tasks::{config::CommonConfig, generic::input_poller::*};
use crate::tasks::{
    coverage::{recorder::CoverageRecorder, total::TotalCoverage},
    utils::default_bool_true,
};
use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{fs::list_files, libfuzzer::LibFuzzer, syncdir::SyncedDir};
use onefuzz_telemetry::{Event::coverage_data, EventData};
use reqwest::Url;
use serde::Deserialize;
use std::collections::{BTreeMap, HashMap};
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
    sync::Arc,
};
use storage_queue::{Message, QueueClient};
use tokio::fs;

const TOTAL_COVERAGE: &str = "total.cov";

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
    pub fn new(config: Config) -> Self {
        let config = Arc::new(config);
        let poller = InputPoller::new("libfuzzer-coverage");
        Self { config, poller }
    }

    pub async fn verify(&self) -> Result<()> {
        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.common.setup_dir,
        );
        fuzzer.verify(self.config.check_fuzzer_help, None).await
    }

    pub async fn managed_run(&mut self) -> Result<()> {
        info!("starting libFuzzer coverage task");
        self.verify().await?;
        self.config.coverage.init_pull().await?;
        self.process().await
    }

    async fn process(&mut self) -> Result<()> {
        let mut processor = CoverageProcessor::new(self.config.clone()).await?;

        info!("processing initial dataset");
        let mut seen_inputs = false;
        // Update the total with the coverage from each seed corpus.
        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.local_path.display());
            dir.init_pull().await?;
            if self.record_corpus_coverage(&mut processor, dir).await? {
                seen_inputs = true;
            }
        }

        if seen_inputs {
            processor.report_total().await?;
            self.config.coverage.sync_push().await?;

            info!(
                "recorded coverage for {} containers in `readonly_inputs`",
                self.config.readonly_inputs.len(),
            );
        } else {
            info!("no initial inputs in `readonly_inputs`",);
        }

        // If a queue has been provided, poll it for new coverage.
        if let Some(queue) = &self.config.input_queue {
            info!("polling queue for new coverage");
            let callback = CallbackImpl::new(queue.clone(), processor)?;
            self.poller.run(callback).await?;
        }

        Ok(())
    }

    async fn record_corpus_coverage(
        &self,
        processor: &mut CoverageProcessor,
        corpus_dir: &SyncedDir,
    ) -> Result<bool> {
        let mut corpus = fs::read_dir(&corpus_dir.local_path)
            .await
            .with_context(|| {
                format!(
                    "unable to read corpus coverage directory: {}",
                    corpus_dir.local_path.display()
                )
            })?;
        let mut seen_inputs = false;

        loop {
            let input = match corpus.next_entry().await {
                Ok(Some(input)) => input,
                Ok(None) => break,
                Err(err) => {
                    error!("{}", err);
                    continue;
                }
            };

            processor.test_input(&input.path()).await?;
            seen_inputs = true;
        }

        Ok(seen_inputs)
    }
}

pub struct CoverageProcessor {
    config: Arc<Config>,
    pub recorder: CoverageRecorder,
    pub total: TotalCoverage,
    pub module_totals: BTreeMap<OsString, TotalCoverage>,
    heartbeat_client: Option<TaskHeartbeatClient>,
}

impl CoverageProcessor {
    pub async fn new(config: Arc<Config>) -> Result<Self> {
        let heartbeat_client = config.common.init_heartbeat().await?;
        let total = TotalCoverage::new(config.coverage.local_path.join(TOTAL_COVERAGE));
        let recorder = CoverageRecorder::new(config.clone()).await?;
        let module_totals = BTreeMap::default();

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

        debug!("updating module info {:?}", module);

        if !self.module_totals.contains_key(&module) {
            let parent = &self.config.coverage.local_path.join("by-module");
            fs::create_dir_all(parent).await.with_context(|| {
                format!(
                    "unable to create by-module coverage directory: {}",
                    parent.display()
                )
            })?;
            let module_total = parent.join(&module);
            let total = TotalCoverage::new(module_total);
            self.module_totals.insert(module.clone(), total);
        }

        self.module_totals[&module].update_bytes(data).await?;

        debug!("updated {:?}", module);
        Ok(())
    }

    async fn collect_by_module(&mut self, path: &Path) -> Result<()> {
        let mut files = list_files(&path).await?;
        files.sort();
        let mut sum = Vec::new();

        for file in &files {
            debug!("checking {:?}", file);
            let mut content = fs::read(file)
                .await
                .with_context(|| format!("unable to read module coverage: {}", file.display()))?;
            self.update_module_total(file, &content).await?;
            sum.append(&mut content);
        }

        let mut combined = path.as_os_str().to_owned();
        combined.push(".cov");

        fs::write(&combined, sum)
            .await
            .with_context(|| format!("unable to write combined coverage file: {:?}", combined))?;

        Ok(())
    }

    pub async fn test_input(&mut self, input: &Path) -> Result<()> {
        info!("processing input {:?}", input);
        let new_coverage = self.recorder.record(input).await?;
        self.collect_by_module(&new_coverage).await?;
        self.update_total().await?;
        Ok(())
    }

    async fn update_total(&mut self) -> Result<()> {
        let mut total = Vec::new();
        for module_total in self.module_totals.values() {
            if let Some(mut module_data) = module_total.data().await? {
                total.append(&mut module_data);
            }
        }
        self.total.write(&total).await?;
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
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat_client.alive();
        self.test_input(input).await?;
        self.report_total().await?;
        self.config.coverage.sync_push().await?;
        Ok(())
    }
}
