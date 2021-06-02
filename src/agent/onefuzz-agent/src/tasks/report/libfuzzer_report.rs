// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::crash_report::*;
use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::*,
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::default_bool_true,
};
use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{blob::BlobUrl, libfuzzer::LibFuzzer, sha256, syncdir::SyncedDir};
use reqwest::Url;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};
use storage_queue::{Message, QueueClient};

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    // TODO:  options are not yet used for crash reporting
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,
    pub input_queue: Option<QueueClient>,
    pub crashes: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(default)]
    pub minimized_stack_depth: Option<usize>,

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
        let poller = InputPoller::new("libfuzzer-crash-report");
        let config = Arc::new(config);

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
        info!("Starting libFuzzer crash report task");
        self.verify().await?;

        if let Some(unique_reports) = &self.config.unique_reports {
            unique_reports.init().await?;
        }
        if let Some(reports) = &self.config.reports {
            reports.init().await?;
        }
        if let Some(no_repro) = &self.config.no_repro {
            no_repro.init().await?;
        }

        let mut processor = AsanProcessor::new(self.config.clone()).await?;

        if let Some(crashes) = &self.config.crashes {
            self.poller.batch_process(&mut processor, &crashes).await?;
        }

        if self.config.check_queue {
            if let Some(url) = &self.config.input_queue {
                let callback = CallbackImpl::new(url.clone(), processor)?;
                self.poller.run(callback).await?;
            }
        }
        Ok(())
    }
}

pub struct TestInputArgs<'a> {
    pub input_url: Option<Url>,
    pub input: &'a Path,
    pub target_exe: &'a Path,
    pub target_options: &'a [String],
    pub target_env: &'a HashMap<String, String>,
    pub setup_dir: &'a Path,
    pub task_id: uuid::Uuid,
    pub job_id: uuid::Uuid,
    pub target_timeout: Option<u64>,
    pub check_retry_count: u64,
    pub minimized_stack_depth: Option<usize>,
}

pub async fn test_input(args: TestInputArgs<'_>) -> Result<CrashTestResult> {
    let fuzzer = LibFuzzer::new(
        args.target_exe,
        args.target_options,
        args.target_env,
        args.setup_dir,
    );

    let task_id = args.task_id;
    let job_id = args.job_id;
    let input_blob = args
        .input_url
        .and_then(|u| BlobUrl::new(u).ok())
        .map(InputBlob::from);
    let input = args.input;
    let input_sha256 = sha256::digest_file(args.input)
        .await
        .with_context(|| format_err!("unable to sha256 digest input file: {}", input.display()))?;

    let test_report = fuzzer
        .repro(args.input, args.target_timeout, args.check_retry_count)
        .await?;

    match test_report.crash_log {
        Some(crash_log) => {
            let crash_report = CrashReport::new(
                crash_log,
                task_id,
                job_id,
                args.target_exe,
                input_blob,
                input_sha256,
                args.minimized_stack_depth,
            );
            Ok(CrashTestResult::CrashReport(crash_report))
        }
        None => {
            let no_repro = NoCrash {
                input_blob,
                input_sha256,
                executable: PathBuf::from(&args.target_exe),
                task_id,
                job_id,
                tries: 1 + args.check_retry_count,
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
        let heartbeat_client = config.common.init_heartbeat(None).await?;

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
        let args = TestInputArgs {
            input_url,
            input,
            target_exe: &self.config.target_exe,
            target_options: &self.config.target_options,
            target_env: &self.config.target_env,
            setup_dir: &self.config.common.setup_dir,
            task_id: self.config.common.task_id,
            job_id: self.config.common.job_id,
            target_timeout: self.config.target_timeout,
            check_retry_count: self.config.check_retry_count,
            minimized_stack_depth: self.config.minimized_stack_depth,
        };
        let result = test_input(args).await?;

        Ok(result)
    }
}

#[async_trait]
impl Processor for AsanProcessor {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()> {
        debug!("processing libfuzzer crash url:{:?} path:{:?}", url, input);
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
