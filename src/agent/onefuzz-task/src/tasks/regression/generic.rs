// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    report::{crash_report::CrashTestResult, generic},
    utils::{default_bool_true, try_resolve_setup_relative_path},
};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use reqwest::Url;
use serde::Deserialize;
use std::{collections::HashMap, path::PathBuf};

use super::common::{self, RegressionHandler};

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
    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,
    pub readonly_inputs: Option<SyncedDir>,

    #[serde(default)]
    pub check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    pub check_debugger: bool,
    #[serde(default)]
    pub check_retry_count: u64,

    #[serde(default)]
    pub minimized_stack_depth: Option<usize>,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct GenericRegressionTask {
    config: Config,
}

#[async_trait]
impl RegressionHandler for GenericRegressionTask {
    async fn get_crash_result(&self, input: PathBuf, input_url: Url) -> Result<CrashTestResult> {
        let target_exe =
            try_resolve_setup_relative_path(&self.config.common.setup_dir, &self.config.target_exe)
                .await?;

        let extra_setup_dir = self.config.common.extra_setup_dir.as_deref();
        let args = generic::TestInputArgs {
            input_url: Some(input_url),
            input: &input,
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
        generic::test_input(args).await
    }
}

impl GenericRegressionTask {
    pub fn new(config: Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        info!("Starting generic regression task");

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
        common::run(
            &self.config.common,
            &self.config.regression_reports,
            &self.config.crashes,
            &report_dirs,
            &self.config.report_list,
            &self.config.readonly_inputs,
            self,
        )
        .await?;
        Ok(())
    }
}
