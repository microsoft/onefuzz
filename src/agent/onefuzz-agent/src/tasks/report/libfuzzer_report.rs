// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::crash_report::*;
use crate::tasks::{config::CommonConfig, generic::input_poller::*, heartbeat::*};
use anyhow::Result;
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

fn default_bool_true() -> bool {
    true
}

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
    pub unique_reports: SyncedDir,
    pub no_repro: Option<SyncedDir>,
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
        let working_dir = config.common.task_id.to_string();
        let poller = InputPoller::new(working_dir);
        let config = Arc::new(config);

        Self { config, poller }
    }

    pub async fn run_local(&mut self) -> Result<()> {
        let mut processor = AsanProcessor::new(self.config.clone()).await?;
        let crashes = match &self.config.crashes {
            Some(x) => x,
            None => bail!("missing crashes directory"),
        };

        self.poller.batch_process(&mut processor, crashes).await?;

        if self.config.check_queue {
            let mut monitor = DirectoryMonitor::new(crashes.path.clone());
            monitor.start()?;

            while let Some(crash) = monitor.next().await {
                let test_url = Url::parse("https://contoso.com/sample-container/blob.txt")?;
                let input_path = Path::new(&crash);
                let result = processor.test_input(test_url, &input_path).await?;
            }
        }

        Ok(())
    }

    pub async fn run_managed(&mut self) -> Result<()> {
        info!("Starting libFuzzer crash report task");
        let mut processor = AsanProcessor::new(self.config.clone()).await?;

        if let Some(crashes) = &self.config.crashes {
            self.poller.batch_process(&mut processor, crashes).await?;
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

    pub async fn test_input(&self, input_url: Url, input: &Path) -> Result<CrashTestResult> {
        self.heartbeat_client.alive();
        let fuzzer = LibFuzzer::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
        );

        let task_id = self.config.common.task_id;
        let job_id = self.config.common.job_id;
        let input_blob = InputBlob::from(BlobUrl::new(input_url)?);
        let input_sha256 = sha256::digest_file(input).await?;

        let test_report = fuzzer
            .repro(
                input,
                self.config.target_timeout,
                self.config.check_retry_count,
            )
            .await?;

        match test_report.asan_log {
            Some(asan_log) => {
                let crash_report = CrashReport::new(
                    asan_log,
                    task_id,
                    job_id,
                    &self.config.target_exe,
                    input_blob,
                    input_sha256,
                );
                Ok(CrashTestResult::CrashReport(crash_report))
            }
            None => {
                let no_repro = NoCrash {
                    input_blob,
                    input_sha256,
                    executable: PathBuf::from(&self.config.target_exe),
                    task_id,
                    job_id,
                    tries: 1 + self.config.check_retry_count,
                    error: test_report.error.map(|e| format!("{}", e)),
                };

                Ok(CrashTestResult::NoRepro(no_repro))
            }
        }
    }
}

#[async_trait]
impl Processor for AsanProcessor {
    async fn process(&mut self, url: Url, input: &Path) -> Result<()> {
        let report = self.test_input(url, input).await?;
        report
            .upload(
                &self.config.unique_reports,
                &self.config.reports,
                &self.config.no_repro,
            )
            .await
    }
}
