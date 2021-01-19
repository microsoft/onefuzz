// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::crash_report::*;
use crate::tasks::{
    config::CommonConfig, generic::input_poller::*, heartbeat::*, utils::default_bool_true,
};
use anyhow::{Context, Result};
use async_trait::async_trait;
use futures::stream::StreamExt;
use onefuzz::{
    blob::BlobUrl, libfuzzer::LibFuzzer, monitor::DirectoryMonitor, sha256, syncdir::SyncedDir,
};
use reqwest::Url;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};
use storage_queue::Message;

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    // TODO:  options are not yet used for crash reporting
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,
    pub input_queue: Option<Url>,
    pub crashes: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub file_list: Vec<String>,
    pub no_repro: Option<SyncedDir>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(default = "default_bool_true")]
    pub check_queue: bool,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct ReportTask {
    config: Arc<Config>,
    pub poller: InputPoller<Message>,
}

impl ReportTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new();
        let config = Arc::new(config);

        Self { config, poller }
    }

    pub async fn local_run(&self) -> Result<()> {
        let mut processor = AsanProcessor::new(self.config.clone()).await?;
        let crashes = match &self.config.crashes {
            Some(x) => x,
            None => bail!("missing crashes directory"),
        };
        crashes.init().await?;

        self.config.unique_reports.init().await?;
        if let Some(reports) = &self.config.reports {
            reports.init().await?;
        }
        if let Some(no_repro) = &self.config.no_repro {
            no_repro.init().await?;
        }

        let mut read_dir = tokio::fs::read_dir(&crashes.path).await.with_context(|| {
            format_err!(
                "unable to read crashes directory {}",
                crashes.path.display()
            )
        })?;

        while let Some(crash) = read_dir.next().await {
            processor.process(None, &crash?.path()).await?;
        }

        if self.config.check_queue {
            let mut monitor = DirectoryMonitor::new(crashes.path.clone());
            monitor.start()?;
            while let Some(crash) = monitor.next().await {
                processor.process(None, &crash).await?;
            }
        }

        Ok(())
    }

    pub async fn managed_run(&mut self) -> Result<()> {
        info!("Starting libFuzzer crash report task");
        let mut processor = AsanProcessor::new(self.config.clone()).await?;

        if let Some(crashes) = &self.config.crashes {
            self.poller
                .batch_process(&mut processor, &crashes, &self.config.file_list)
                .await?;
        }

        if self.config.check_queue {
            if let Some(queue) = &self.config.input_queue {
                let callback = CallbackImpl::new(queue.clone(), processor);
                self.poller.run(callback).await?;
            }
        }
        Ok(())
    }
}

pub async fn test_input(
    input_url: Url,
    input: &Path,
    target_exe: &Path,
    target_options: &[String],
    target_env: &HashMap<String, String>,
    setup_dir: &Path,
    task_id: uuid::Uuid,
    job_id: uuid::Uuid,
    target_timeout: Option<u64>,
    check_retry_count: u64,
) -> Result<CrashTestResult> {
    let fuzzer = LibFuzzer::new(target_exe, target_options, target_env, setup_dir);

    let task_id = task_id;
    let job_id = job_id;
    let input_blob = InputBlob::from(BlobUrl::new(input_url)?);
    let input_sha256 = sha256::digest_file(input).await.with_context(|| {
        format_err!("unable to sha256 digest input file: {}", input.display())
    })?;

    let test_report = fuzzer
        .repro(input, target_timeout, check_retry_count)
        .await?;

    match test_report.asan_log {
        Some(asan_log) => {
            let crash_report = CrashReport::new(
                asan_log,
                task_id,
                job_id,
                target_exe,
                input_blob,
                input_sha256,
            );
            Ok(CrashTestResult::CrashReport(crash_report))
        }
        None => {
            let no_repro = NoCrash {
                input_blob,
                input_sha256,
                executable: PathBuf::from(&target_exe),
                task_id,
                job_id,
                tries: 1 + check_retry_count,
                error: test_report.error.map(|e| format!("{}", e)),
            };

            Ok(CrashTestResult::NoRepro(no_repro))
        }
    }
}

pub struct AsanProcessor {
    config: Arc<Config>,
    heartbeat_client: Option<TaskHeartbeatClient>,
}

impl AsanProcessor {
    pub async fn new(config: Arc<Config>) -> Result<Self> {
        let heartbeat_client = config.common.init_heartbeat().await?;

        Ok(Self {
            config,
            heartbeat_client,
        })
    }

    pub async fn test_input(
        &self,
        input_url: Option<Url>,
        input: &Path,
    ) -> Result<CrashTestResult> {
        self.heartbeat_client.alive();
        let result = test_input(
            input_url,
            input,
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.common.setup_dir,
            self.config.common.task_id,
            self.config.common.job_id,
            self.config.target_timeout,
            self.config.check_retry_count,
        )
        .await?;

        Ok(result)
    }
}

#[async_trait]
impl Processor for AsanProcessor {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()> {
        verbose!("processing libfuzzer crash url:{:?} path:{:?}", url, input);
        let report = self.test_input(url, input).await?;
        report
            .save(
                &self.config.unique_reports,
                &self.config.reports,
                &self.config.no_repro,
            )
            .await
    }
}
