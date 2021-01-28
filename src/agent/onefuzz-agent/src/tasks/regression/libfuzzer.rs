// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::report::{crash_report::CrashTestResult, libfuzzer_report};

use anyhow::Result;
use reqwest::Url;
use std::path::PathBuf;

use super::regression::{self, Config};

pub struct LibFuzzerRegressionTask<'a> {
    config: &'a Config,
}

pub async fn get_crash_result<'a>(
    config: &'a Config,
    input: PathBuf,
    input_url: Option<Url>,
) -> Result<CrashTestResult> {
    libfuzzer_report::test_input(
        input_url,
        input.as_ref(),
        &config.target_exe,
        &config.target_options,
        &config.target_env,
        &config.common.setup_dir,
        config.common.task_id,
        config.common.job_id,
        config.target_timeout,
        config.check_retry_count,
    )
    .await
}

impl<'a> LibFuzzerRegressionTask<'a> {
    pub fn new(config: &'a Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        info!("Starting libfuzzer regression task");
        regression::run(&self.config, get_crash_result).await?;
        Ok(())
    }
}
