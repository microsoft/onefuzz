// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};

use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{blob::BlobUrl, sha256, syncdir::SyncedDir};
use reqwest::Url;
use serde::Deserialize;
use storage_queue::{Message, QueueClient};

use crate::tasks::report::crash_report::*;
use crate::tasks::report::dotnet::common::collect_exception_info;
use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::*,
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::default_bool_true,
};

const DOTNET_DUMP_TOOL_NAME: &str = "dotnet-dump";

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

pub struct DotnetCrashReportTask {
    config: Arc<Config>,
    pub poller: InputPoller<Message>,
}

impl DotnetCrashReportTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("libfuzzer-dotnet-crash-report");
        let config = Arc::new(config);

        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting dotnet crash report task");

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
            self.poller.batch_process(&mut processor, crashes).await?;
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
        input: &Path,
        input_url: Option<Url>,
    ) -> Result<CrashTestResult> {
        self.heartbeat_client.alive();

        let input_blob = input_url
            .and_then(|u| BlobUrl::new(u).ok())
            .map(InputBlob::from);

        let input_sha256 = sha256::digest_file(input).await.with_context(|| {
            format_err!("unable to sha256 digest input file: {}", input.display())
        })?;

        let job_id = self.config.common.task_id;
        let task_id = self.config.common.task_id;
        let executable = self.config.common.setup_dir.join(&self.config.target_exe);

        let mut args = vec!["dotnet".to_owned(), executable.display().to_string()];
        args.extend(self.config.target_options.clone());

        let env = self.config.target_env.clone();

        let crash_test_result = if let Some(exception) = collect_exception_info(&args, env).await? {
            let call_stack_sha256 = stacktrace_parser::digest_iter(&exception.call_stack, None);

            let crash_report = CrashReport {
                input_sha256,
                input_blob,
                executable,
                crash_type: exception.exception,
                crash_site: exception.call_stack[0].clone(),
                call_stack: exception.call_stack,
                call_stack_sha256,
                minimized_stack: None,
                minimized_stack_sha256: None,
                minimized_stack_function_names: None,
                minimized_stack_function_names_sha256: None,
                minimized_stack_function_lines: None,
                minimized_stack_function_lines_sha256: None,
                asan_log: None,
                task_id,
                job_id,
                scariness_score: None,
                scariness_description: None,
                onefuzz_version: Some(env!("ONEFUZZ_VERSION").to_owned()),
                tool_name: Some(DOTNET_DUMP_TOOL_NAME.to_owned()),
                tool_version: None,
            };

            crash_report.into()
        } else {
            let no_repro = NoCrash {
                input_sha256,
                input_blob,
                executable,
                job_id,
                task_id,
                tries: 1,
                error: None,
            };

            no_repro.into()
        };

        Ok(crash_test_result)
    }
}

#[async_trait]
impl Processor for AsanProcessor {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()> {
        debug!("processing dotnet crash url:{:?} path:{:?}", url, input);

        let crash_test_result = self.test_input(input, url).await?;

        let saved = crash_test_result
            .save(
                &self.config.unique_reports,
                &self.config.reports,
                &self.config.no_repro,
            )
            .await;

        if let Err(err) = saved {
            error!(
                "error saving crash test result for input \"{}\": {}",
                input.display(),
                err,
            );
        }

        Ok(())
    }
}
