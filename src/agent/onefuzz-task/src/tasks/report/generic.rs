// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::crash_report::{CrashReport, CrashTestResult, InputBlob, NoCrash};
use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::{CallbackImpl, InputPoller, Processor},
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::{default_bool_true, try_resolve_setup_relative_path},
};
use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{
    blob::BlobUrl, input_tester::Tester, machine_id::MachineIdentity, sha256, syncdir::SyncedDir,
};
use onefuzz_result::job_result::TaskJobResultClient;
use reqwest::Url;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
use storage_queue::{Message, QueueClient};
use uuid::Uuid;

const GENERIC_TOOL_NAME: &str = "generic";

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,

    #[serde(default)]
    pub target_options: Vec<String>,

    #[serde(default)]
    pub target_env: HashMap<String, String>,

    pub input_queue: Option<QueueClient>,
    pub crashes: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,

    pub target_timeout: Option<u64>,

    #[serde(default)]
    pub check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    pub check_debugger: bool,
    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(default = "default_bool_true")]
    pub check_queue: bool,

    #[serde(default)]
    pub minimized_stack_depth: Option<usize>,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct ReportTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl ReportTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("crash-report");
        Self { config, poller }
    }

    pub async fn managed_run(&mut self) -> Result<()> {
        info!("Starting generic crash report task");
        let heartbeat_client = self.config.common.init_heartbeat(None).await?;
        let job_result_client = self.config.common.init_job_result().await?;
        let mut processor =
            GenericReportProcessor::new(&self.config, heartbeat_client, job_result_client);

        #[allow(clippy::manual_flatten)]
        for entry in [
            &self.config.reports,
            &self.config.unique_reports,
            &self.config.no_repro,
        ] {
            if let Some(entry) = entry {
                tokio::fs::create_dir_all(&entry.local_path).await?;
            }
        }

        info!("processing existing crashes");
        if let Some(crashes) = &self.config.crashes {
            self.poller
                .batch_process(&mut processor, crashes)
                .await
                .context("batch processing failed")?;
        }

        info!("processing crashes from queue");
        if self.config.check_queue {
            if let Some(queue) = &self.config.input_queue {
                let callback = CallbackImpl::new(queue.clone(), processor)
                    .context("processing from queue failed")?;
                self.poller.run(callback).await.context("poller failed")?;
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
    pub extra_setup_dir: Option<&'a Path>,
    pub task_id: Uuid,
    pub job_id: Uuid,
    pub target_timeout: Option<u64>,
    pub check_retry_count: u64,
    pub check_asan_log: bool,
    pub check_debugger: bool,
    pub minimized_stack_depth: Option<usize>,
    pub machine_identity: MachineIdentity,
}

pub async fn test_input(args: TestInputArgs<'_>) -> Result<CrashTestResult> {
    let extra_setup_dir = args.extra_setup_dir;
    let tester = Tester::new(
        args.setup_dir,
        extra_setup_dir,
        args.target_exe,
        args.target_options,
        args.target_env,
        args.machine_identity.clone(),
    )
    .check_asan_log(args.check_asan_log)
    .check_debugger(args.check_debugger)
    .check_retry_count(args.check_retry_count)
    .set_optional(args.target_timeout, |tester, timeout| {
        tester.timeout(timeout)
    });

    let input_sha256 = sha256::digest_file(args.input).await?;
    let task_id = args.task_id;
    let job_id = args.job_id;
    let input_blob = args
        .input_url
        .and_then(|u| BlobUrl::new(u).ok())
        .map(InputBlob::from);

    let test_report = tester.test_input(args.input).await?;

    if let Some(crash_log) = test_report.crash_log {
        let crash_report = CrashReport::new(
            crash_log,
            task_id,
            job_id,
            args.target_exe,
            input_blob,
            input_sha256,
            args.minimized_stack_depth,
            GENERIC_TOOL_NAME.into(),
            env!("ONEFUZZ_VERSION").to_string(),
            env!("ONEFUZZ_VERSION").to_string(),
        );
        Ok(CrashTestResult::CrashReport(Box::new(crash_report)))
    } else {
        let no_repro = NoCrash {
            input_blob,
            input_sha256,
            executable: PathBuf::from(args.target_exe),
            task_id,
            job_id,
            tries: 1 + args.check_retry_count,
            error: test_report.error.map(|e| format!("{e}")),
        };

        Ok(CrashTestResult::NoRepro(Box::new(no_repro)))
    }
}

pub struct GenericReportProcessor<'a> {
    config: &'a Config,
    heartbeat_client: Option<TaskHeartbeatClient>,
    job_result_client: Option<TaskJobResultClient>,
}

impl<'a> GenericReportProcessor<'a> {
    pub fn new(
        config: &'a Config,
        heartbeat_client: Option<TaskHeartbeatClient>,
        job_result_client: Option<TaskJobResultClient>,
    ) -> Self {
        Self {
            config,
            heartbeat_client,
            job_result_client,
        }
    }

    pub async fn test_input(
        &self,
        input_url: Option<Url>,
        input: &Path,
    ) -> Result<CrashTestResult> {
        self.heartbeat_client.alive();

        let target_exe =
            try_resolve_setup_relative_path(&self.config.common.setup_dir, &self.config.target_exe)
                .await?;

        let extra_setup_dir = self.config.common.extra_setup_dir.as_deref();
        let args = TestInputArgs {
            input_url,
            input,
            target_exe: &target_exe,
            target_options: &self.config.target_options,
            target_env: &self.config.target_env,
            setup_dir: &self.config.common.setup_dir,
            extra_setup_dir,
            task_id: self.config.common.task_id,
            job_id: self.config.common.job_id,
            target_timeout: self.config.target_timeout,
            check_retry_count: self.config.check_retry_count,
            check_asan_log: self.config.check_asan_log,
            check_debugger: self.config.check_debugger,
            minimized_stack_depth: self.config.minimized_stack_depth,
            machine_identity: self.config.common.machine_identity.clone(),
        };
        test_input(args).await.context("test input failed")
    }
}

#[async_trait]
impl<'a> Processor for GenericReportProcessor<'a> {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()> {
        debug!("generating crash report for: {}", input.display());
        let report = self
            .test_input(url, input)
            .await
            .context("test input failed")?;
        report
            .save(
                &self.config.unique_reports,
                &self.config.reports,
                &self.config.no_repro,
                &self.job_result_client,
            )
            .await
            .context("saving report failed")
    }
}
