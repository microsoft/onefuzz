// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::crash_report::{CrashReport, CrashTestResult, InputBlob, NoCrash};
use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::{CallbackImpl, InputPoller, Processor},
    heartbeat::*,
};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::{blob::BlobUrl, input_tester::Tester, sha256, syncdir::SyncedDir};
use reqwest::Url;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
use storage_queue::Message;

fn default_bool_true() -> bool {
    true
}
#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,

    #[serde(default)]
    pub target_options: Vec<String>,

    #[serde(default)]
    pub target_env: HashMap<String, String>,

    pub input_queue: Option<Url>,
    pub crashes: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,
    pub unique_reports: SyncedDir,
    pub no_repro: Option<SyncedDir>,

    pub target_timeout: Option<u64>,

    #[serde(default)]
    pub check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    pub check_debugger: bool,
    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct ReportTask<'a> {
    config: &'a Config,
    poller: InputPoller<Message>,
}

impl<'a> ReportTask<'a> {
    pub fn new(config: &'a Config) -> Self {
        let working_dir = config.common.task_id.to_string();
        let poller = InputPoller::new(working_dir);

        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("Starting generic crash report task");
        let heartbeat_client = self.config.common.init_heartbeat().await?;
        let mut processor = GenericReportProcessor::new(&self.config, heartbeat_client);

        if let Some(crashes) = &self.config.crashes {
            self.poller.batch_process(&mut processor, &crashes).await?;
        }

        if let Some(queue) = &self.config.input_queue {
            let callback = CallbackImpl::new(queue.clone(), processor);
            self.poller.run(callback).await?;
        }
        Ok(())
    }
}

pub struct GenericReportProcessor<'a> {
    config: &'a Config,
    tester: Tester<'a>,
    heartbeat_client: Option<TaskHeartbeatClient>,
}

impl<'a> GenericReportProcessor<'a> {
    pub fn new(config: &'a Config, heartbeat_client: Option<TaskHeartbeatClient>) -> Self {
        let tester = Tester::new(
            &config.target_exe,
            &config.target_options,
            &config.target_env,
            &config.target_timeout,
            config.check_asan_log,
            false,
            config.check_debugger,
            config.check_retry_count,
        );

        Self {
            config,
            tester,
            heartbeat_client,
        }
    }

    pub async fn test_input(
        &self,
        input_url: Option<Url>,
        input: &Path,
    ) -> Result<CrashTestResult> {
        self.heartbeat_client.alive();
        let input_sha256 = sha256::digest_file(input).await?;
        let task_id = self.config.common.task_id;
        let job_id = self.config.common.job_id;
        let input_blob = match input_url {
            Some(x) => Some(InputBlob::from(BlobUrl::new(x)?)),
            None => None,
        };

        let test_report = self.tester.test_input(input).await?;

        if let Some(asan_log) = test_report.asan_log {
            let crash_report = CrashReport::new(
                asan_log,
                task_id,
                job_id,
                &self.config.target_exe,
                input_blob,
                input_sha256,
            );
            Ok(CrashTestResult::CrashReport(crash_report))
        } else if let Some(crash) = test_report.crash {
            let call_stack_sha256 = sha256::digest_iter(&crash.call_stack);
            let crash_report = CrashReport {
                input_blob,
                input_sha256,
                executable: PathBuf::from(&self.config.target_exe),
                call_stack: crash.call_stack,
                crash_type: crash.crash_type,
                crash_site: crash.crash_site,
                call_stack_sha256,
                asan_log: None,
                scariness_score: None,
                scariness_description: None,
                task_id,
                job_id,
            };

            Ok(CrashTestResult::CrashReport(crash_report))
        } else {
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

#[async_trait]
impl<'a> Processor for GenericReportProcessor<'a> {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()> {
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
