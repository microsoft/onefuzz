// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    collections::HashMap,
    env,
    path::{Path, PathBuf},
    sync::Arc,
};

use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::expand::Expand;
use onefuzz::fs::set_executable;
use onefuzz::{blob::BlobUrl, sha256, syncdir::SyncedDir};
use reqwest::Url;
use serde::Deserialize;
use storage_queue::{Message, QueueClient};
use tokio::fs;

use crate::tasks::report::crash_report::*;
use crate::tasks::report::dotnet::common::collect_exception_info;
use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::*,
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::{default_bool_true, try_resolve_setup_relative_path},
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
    pub tools: SyncedDir,

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

        self.config.tools.init_pull().await?;

        set_executable(&self.config.tools.local_path).await?;

        if let Some(crashes) = &self.config.crashes {
            crashes.init().await?;
        }

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

    async fn target_exe(&self) -> Result<String> {
        let tools_dir = self.config.tools.local_path.to_string_lossy().into_owned();

        // Try to expand `target_exe` with support for `{tools_dir}`.
        //
        // Allows using `LibFuzzerDotnetLoader.exe` from a shared tools container.
        let expand = Expand::new(&self.config.common.machine_identity).tools_dir(tools_dir);
        let expanded = expand.evaluate_value(self.config.target_exe.to_string_lossy())?;
        let expanded_path = Path::new(&expanded);

        // Check if `target_exe` was resolved to an absolute path and an existing file.
        // If so, then the user specified a `target_exe` under the `tools` dir.
        let is_absolute = expanded_path.is_absolute();
        let file_exists = fs::metadata(&expanded).await.is_ok();

        if is_absolute && file_exists {
            // We have found `target_exe`, so skip `setup`-relative expansion.
            return Ok(expanded);
        }

        // We haven't yet resolved a local path for `target_exe`. Try the usual
        // `setup`-relative interpretation of the configured value of `target_exe`.
        let resolved = try_resolve_setup_relative_path(&self.config.common.setup_dir, expanded)
            .await?
            .to_string_lossy()
            .into_owned();

        Ok(resolved)
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

        let job_id = self.config.common.job_id;
        let task_id = self.config.common.task_id;

        let target_exe = self.target_exe().await?;
        let executable = PathBuf::from(&target_exe);

        let mut args = vec![target_exe];
        args.extend(self.config.target_options.clone());

        let expand = Expand::new(&self.config.common.machine_identity)
            .input_path(input)
            .setup_dir(&self.config.common.setup_dir)
            .set_optional_ref(&self.config.common.extra_setup_dir, Expand::extra_setup_dir)
            .set_optional_ref(&self.config.common.extra_output, |expand, value| {
                expand.extra_output_dir(value.local_path.as_path())
            });

        let expanded_args = expand.evaluate(&args)?;

        let env = {
            let mut new = HashMap::new();

            for (k, v) in &self.config.target_env {
                let ev = expand.evaluate_value(v)?;
                new.insert(k, ev);
            }

            new
        };

        let crash_test_result =
            if let Some(exception) = collect_exception_info(&expanded_args, env).await? {
                let call_stack_sha256 = stacktrace_parser::digest_iter(&exception.call_stack, None);

                let crash_report = CrashReport {
                    input_sha256,
                    input_blob,
                    executable,
                    crash_type: exception.exception,
                    crash_site: exception.call_stack.first().cloned().unwrap_or_default(),
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
                &self.heartbeat_client,
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
