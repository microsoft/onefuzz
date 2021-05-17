// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    report::{crash_report::CrashTestResult, libfuzzer_report},
    utils::default_bool_true,
};

use anyhow::{Context, Result};
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

    pub target_timeout: Option<u64>,

    pub crashes: SyncedDir,
    pub regression_reports: SyncedDir,
    pub report_list: Option<Vec<String>>,
    pub unique_reports: Option<SyncedDir>,
    pub reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,
    pub readonly_inputs: Option<SyncedDir>,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,
    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(default)]
    pub minimized_stack_depth: Option<usize>,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct LibFuzzerRegressionTask {
    config: Config,
}

#[async_trait]
impl RegressionHandler for LibFuzzerRegressionTask {
    async fn get_crash_result(&self, input: PathBuf, input_url: Url) -> Result<CrashTestResult> {
        let args = libfuzzer_report::TestInputArgs {
            input_url: Some(input_url),
            input: &input,
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
        libfuzzer_report::test_input(args).await
    }
}

impl LibFuzzerRegressionTask {
    pub fn new(config: Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        let mut report_dirs = vec![];
        for dir in vec![
            &self.config.reports,
            &self.config.unique_reports,
            &self.config.no_repro,
        ]
        .into_iter()
        .flatten()
        {
            report_dirs.push(dir);
        }

        let heartbeat_client = self.config.common.init_heartbeat().await?;
        common::run(
            heartbeat_client,
            &self.config.regression_reports,
            &self.config.crashes,
            &report_dirs,
            &self.config.report_list,
            &self.config.readonly_inputs,
            self,
        )
        .await
        .context("libfuzzer regression")?;
        Ok(())
    }
}
