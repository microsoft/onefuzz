// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::*,
    report::{
        crash_report::{CrashReport, CrashTestResult},
        generic, libfuzzer_report,
    },
    utils::{default_bool_true, download_input},
};
use anyhow::Result;
use onefuzz::syncdir::{self, SyncedDir};
use reqwest::Url;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,

    #[serde(default)]
    pub target_options: Vec<String>,

    #[serde(default)]
    pub target_env: HashMap<String, String>,

    pub inputs: Option<SyncedDir>,

    pub input_reports: Option<SyncedDir>,
    pub crashes: Option<SyncedDir>,
    pub report_list: Vec<String>,

    pub no_repro: SyncedDir,
    pub reports: SyncedDir,

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

pub struct GenericRegressionTask<'a> {
    config: &'a Config,
}

impl<'a> GenericRegressionTask<'a> {
    pub fn new(config: &'a Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        info!("Starting generic regression task");
        let heartbeat_client = self.config.common.init_heartbeat().await?;
        if let (Some(input_reports), Some(crashes)) =
            (&self.config.input_reports, &self.config.crashes)
        {
            self.handle_crash_reports(&heartbeat_client, input_reports, crashes)
                .await?;
        }

        if let Some(inputs) = &self.config.inputs {
            self.handle_inputs(inputs, &heartbeat_client).await?;
        }

        Ok(())
    }

    pub async fn handle_inputs(
        &self,
        inputs: &SyncedDir,
        heartbeat_client: &Option<TaskHeartbeatClient>,
    ) -> Result<()> {
        inputs.sync_pull().await?;
        let mut input_files = tokio::fs::read_dir(&inputs.path).await?;
        while let Some(file) = input_files.next_entry().await? {
            heartbeat_client.alive();
            let input_url = inputs.url.clone().and_then(|container_url| {
                file.file_name()
                    .to_str()
                    .and_then(|f_name| container_url.url().join(f_name).ok())
            });
            let report = self
                .get_crash_result(file.path(), input_url, self.config.check_asan_log)
                .await?;

            let reports = Some(self.config.reports.clone());
            let no_repro = Some(self.config.no_repro.clone());
            report
                .save_regression(
                    None,
                    &reports,
                    &no_repro,
                    format!("{}/", self.config.common.task_id),
                )
                .await?;
        }

        Ok(())
    }

    pub async fn handle_crash_reports(
        &self,
        heartbeat_client: &Option<TaskHeartbeatClient>,
        input_reports: &SyncedDir,
        crashes: &SyncedDir,
    ) -> Result<()> {
        if self.config.report_list.is_empty() {
            input_reports.sync_pull().await?;
        } else {
            for file in &self.config.report_list {
                let input_url = input_reports
                    .url
                    .clone()
                    .ok_or(format_err!("no input url"))?
                    .blob(file);
                download_input(input_url.url(), &input_reports.path).await?;
            }
        }

        let mut report_files = tokio::fs::read_dir(&input_reports.path).await?;
        while let Some(file) = report_files.next_entry().await? {
            heartbeat_client.alive();
            let crash_report_str = std::fs::read_to_string(file.path())?;
            let crash_report: CrashReport = serde_json::from_str(&crash_report_str)?;
            let input_url = crash_report
                .input_blob
                .clone()
                .map(|b| b.name)
                .and_then(|crash_name| crashes.url.clone().map(|u| u.blob(crash_name).url()));

            if let Some(input_url) = input_url {
                let input = download_input(input_url.clone(), &crashes.path).await?;
                let report = self
                    .get_crash_result(input, Some(input_url), crash_report.asan_log.is_some())
                    .await?;
                let reports = Some(self.config.reports.clone());
                let no_repro = Some(self.config.no_repro.clone());
                report
                    .save_regression(
                        Some(crash_report),
                        &reports,
                        &no_repro,
                        format!("{}/", self.config.common.task_id),
                    )
                    .await?;
            }
        }
        Ok(())
    }

    pub async fn get_crash_result(
        &self,
        input: impl AsRef<Path>,
        input_url: Option<Url>,
        is_asan: bool,
    ) -> Result<CrashTestResult> {
        if is_asan {
            libfuzzer_report::test_input(
                input_url,
                input.as_ref(),
                &self.config.target_exe,
                &self.config.target_options,
                &self.config.target_env,
                &self.config.common.setup_dir,
                self.config.common.task_id,
                self.config.common.job_id,
                self.config.target_timeout,
                self.config.check_retry_count,
            )
            .await
        } else {
            generic::test_input(
                input_url,
                input.as_ref(),
                &self.config.target_exe,
                &self.config.target_options,
                &self.config.target_env,
                &self.config.common.setup_dir,
                self.config.common.task_id,
                self.config.common.job_id,
                self.config.target_timeout,
                self.config.check_retry_count,
                self.config.check_asan_log,
                self.config.check_debugger,
            )
            .await
        }
    }
}
