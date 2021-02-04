// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    report::{
        crash_report::{CrashReport, CrashTestResult},
        libfuzzer_report,
    },
    utils::default_bool_true,
};

use anyhow::Result;
use reqwest::Url;

use super::common::{self, RegressionHandler};
use async_trait::async_trait;
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

    pub inputs: Option<SyncedDir>,

    pub input_reports: Option<SyncedDir>,
    pub crashes: Option<SyncedDir>,

    #[serde(default)]
    pub report_list: Vec<String>,

    pub no_repro: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,

    pub target_timeout: Option<u64>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,
    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct LibFuzzerRegressionTask {
    config: Config,
}

#[async_trait]
impl RegressionHandler for LibFuzzerRegressionTask {
    async fn get_crash_result(
        &self,
        input: PathBuf,
        input_url: Option<Url>,
    ) -> Result<CrashTestResult> {

        let args = libfuzzer_report::TestInputArgs{
            input_url,
            input: &input,
            target_exe: &self.config.target_exe,
            target_options: &self.config.target_options,
            target_env: &self.config.target_env,
            setup_dir: &self.config.common.setup_dir,
            task_id: self.config.common.task_id,
            job_id: self.config.common.job_id,
            target_timeout: self.config.target_timeout,
            check_retry_count: self.config.check_retry_count,

        };
        libfuzzer_report::test_input(args
        )
        .await
    }

    async fn save_regression(
        &self,
        crash_result: CrashTestResult,
        original_report: Option<CrashReport>,
    ) -> Result<()> {
        crash_result
            .save_regression(
                original_report,
                &self.config.reports,
                &self.config.no_repro,
                format!("{}/", self.config.common.task_id),
            )
            .await
    }
}

impl LibFuzzerRegressionTask {
    pub fn new(config: Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        info!("Starting libfuzzer regression task");
        let heartbeat_client = self.config.common.init_heartbeat().await?;
        common::run(
            heartbeat_client,
            &self.config.input_reports,
            &self.config.report_list,
            &self.config.crashes,
            &self.config.inputs,
            self,
        )
        .await?;
        Ok(())
    }
}
