// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::*,
    report::{crash_report::CrashReport, generic, libfuzzer_report},
    utils::{default_bool_true, download_input},
};
use anyhow::Result;
use onefuzz::syncdir::SyncedDir;
use serde::Deserialize;
use std::{collections::HashMap, path::PathBuf};

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,

    #[serde(default)]
    pub target_options: Vec<String>,

    #[serde(default)]
    pub target_env: HashMap<String, String>,

    pub input_reports: SyncedDir,
    pub crashes: SyncedDir,
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

        self.config.input_reports.init().await?;
        self.config.crashes.init().await?;

        if self.config.report_list.is_empty() {
            self.config.input_reports.sync_pull().await?;
        } else {
            for file in &self.config.report_list {
                let input_url = self
                    .config
                    .input_reports
                    .url
                    .clone()
                    .ok_or(format_err!("no input url"))?
                    .blob(file);
                download_input(input_url.url(), &self.config.input_reports.path).await?;
            }
        }

        let mut report_files = tokio::fs::read_dir(&self.config.input_reports.path).await?;
        while let Some(file) = report_files.next_entry().await? {
            heartbeat_client.alive();
            let crash_report_str = std::fs::read_to_string(file.path())?;
            let crash_report: CrashReport = serde_json::from_str(&crash_report_str)?;
            let input_url = crash_report
                .input_blob
                .ok_or(format_err!("no input url"))?
                .blob_url()?
                .url();

            let input = download_input(input_url.clone(), &self.config.crashes.path).await?;

            let report = match crash_report.asan_log {
                Some(_) => {
                    libfuzzer_report::test_input(
                        Some(input_url),
                        &input,
                        &self.config.target_exe,
                        &self.config.target_options,
                        &self.config.target_env,
                        &self.config.common.setup_dir,
                        self.config.common.task_id,
                        self.config.common.job_id,
                        self.config.target_timeout,
                        self.config.check_retry_count,
                    )
                    .await?
                }
                None => {
                    generic::test_input(
                        Some(input_url),
                        &input,
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
                    .await?
                }
            };

            let reports = Some(self.config.reports.clone());
            let no_repro = Some(self.config.no_repro.clone());

            report.save(&None, &reports, &no_repro).await?;
        }

        Ok(())
    }
}
