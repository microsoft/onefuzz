// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    report::crash_report::{parse_report_file, CrashTestResult, RegressionReport},
};
use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use reqwest::Url;
use std::path::PathBuf;

/// Defines implementation-provided callbacks for all implementers of regression tasks.
///
/// Shared regression task behavior is implemented in this module.
#[async_trait]
pub trait RegressionHandler {
    /// Test the provided input and generate a crash result
    /// * `input` - path to the input to test
    /// * `input_url` - input url
    async fn get_crash_result(&self, input: PathBuf, input_url: Url) -> Result<CrashTestResult>;
}

/// Runs the regression task
pub async fn run(
    heartbeat_client: Option<TaskHeartbeatClient>,
    regression_reports: &SyncedDir,
    crashes: &SyncedDir,
    report_dirs: &[&SyncedDir],
    report_list: &Option<Vec<String>>,
    readonly_inputs: &Option<SyncedDir>,
    handler: &impl RegressionHandler,
) -> Result<()> {
    info!("starting regression task");
    regression_reports.init().await?;

    handle_crash_reports(
        handler,
        crashes,
        report_dirs,
        report_list,
        &regression_reports,
        &heartbeat_client,
    )
    .await
    .context("handling crash reports")?;

    if let Some(readonly_inputs) = &readonly_inputs {
        handle_inputs(
            handler,
            readonly_inputs,
            &regression_reports,
            &heartbeat_client,
        )
        .await
        .context("handling inputs")?;
    }

    info!("regression task stopped");
    Ok(())
}

/// Run the regression on the files in the 'inputs' location
/// * `handler` - regression handler
/// * `readonly_inputs` - location of the input files
/// * `regression_reports` - where reports should be saved
/// * `heartbeat_client` - heartbeat client
pub async fn handle_inputs(
    handler: &impl RegressionHandler,
    readonly_inputs: &SyncedDir,
    regression_reports: &SyncedDir,
    heartbeat_client: &Option<TaskHeartbeatClient>,
) -> Result<()> {
    readonly_inputs.init_pull().await?;
    let mut input_files = tokio::fs::read_dir(&readonly_inputs.local_path).await?;
    while let Some(file) = input_files.next_entry().await? {
        heartbeat_client.alive();

        let file_path = file.path();
        if !file_path.is_file() {
            continue;
        }

        let file_name = file_path
            .file_name()
            .ok_or_else(|| format_err!("missing filename"))?
            .to_string_lossy()
            .to_string();

        let input_url = readonly_inputs.remote_url()?.url()?.join(&file_name)?;

        let crash_test_result = handler.get_crash_result(file_path, input_url).await?;
        RegressionReport {
            crash_test_result,
            original_crash_test_result: None,
        }
        .save(None, regression_reports)
        .await?
    }

    Ok(())
}

pub async fn handle_crash_reports(
    handler: &impl RegressionHandler,
    crashes: &SyncedDir,
    report_dirs: &[&SyncedDir],
    report_list: &Option<Vec<String>>,
    regression_reports: &SyncedDir,
    heartbeat_client: &Option<TaskHeartbeatClient>,
) -> Result<()> {
    // without crash report containers, skip this method
    if report_dirs.is_empty() {
        return Ok(());
    }

    crashes.init_pull().await?;

    for possible_dir in report_dirs {
        possible_dir.init_pull().await?;

        let mut report_files = tokio::fs::read_dir(&possible_dir.local_path).await?;
        while let Some(file) = report_files.next_entry().await? {
            heartbeat_client.alive();
            let file_path = file.path();
            if !file_path.is_file() {
                continue;
            }

            let file_name = file_path
                .file_name()
                .ok_or_else(|| format_err!("missing filename"))?
                .to_string_lossy()
                .to_string();

            if let Some(report_list) = &report_list {
                if !report_list.contains(&file_name) {
                    continue;
                }
            }

            let original_crash_test_result = parse_report_file(file.path())
                .await
                .with_context(|| format!("unable to parse crash report: {}", file_name))?;

            let input_blob = match &original_crash_test_result {
                CrashTestResult::CrashReport(x) => x.input_blob.clone(),
                CrashTestResult::NoRepro(x) => x.input_blob.clone(),
            }
            .ok_or_else(|| format_err!("crash report is missing input blob: {}", file_name))?;

            let input_url = crashes.remote_url()?.url()?;
            let input = crashes.local_path.join(&input_blob.name);
            let crash_test_result = handler.get_crash_result(input, input_url).await?;

            RegressionReport {
                crash_test_result,
                original_crash_test_result: Some(original_crash_test_result),
            }
            .save(Some(file_name), regression_reports)
            .await?
        }
    }

    Ok(())
}
