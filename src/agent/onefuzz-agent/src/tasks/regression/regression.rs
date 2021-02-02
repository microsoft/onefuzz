// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    report::crash_report::{CrashReport, CrashTestResult},
    utils::download_input,
};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use reqwest::Url;
use std::path::PathBuf;

/// Abstraction for handling regression reports
#[async_trait]
pub trait RegressionHandler {

    /// Test the provided input ang generate a crash result
    /// * `input` - path to the input to test
    /// * `input_url` - input url
    async fn get_crash_result(
        &self,
        input: PathBuf,
        input_url: Option<Url>,
    ) -> Result<CrashTestResult>;


    /// Saves a regression
    /// * `crash_result` - crash result to save
    /// * `original_report` - original report used to generate the report
    async fn save_regression(
        &self,
        crash_result: CrashTestResult,
        original_report: Option<CrashReport>,
    ) -> Result<()>;
}


/// Runs the regression task
/// * `heartbeat_client` - heartbeat client
/// * `input_reports` - location of the reports used in this regression run
/// * `report_list` - list of report file names selected to be used in the regression
/// * `crashes` - location of the crash files referenced by the reports in input_reports
/// * `inputs` - location of the input files
/// * `handler` - regression handler
pub async fn run(
    heartbeat_client: Option<TaskHeartbeatClient>,
    input_reports: &Option<SyncedDir>,
    report_list: &[String],
    crashes: &Option<SyncedDir>,
    inputs: &Option<SyncedDir>,
    handler: &impl RegressionHandler,
) -> Result<()> {
    info!("Starting generic regression task");
    if let (Some(input_reports), Some(crashes)) = (input_reports, crashes) {
        handle_crash_reports(
            &heartbeat_client,
            &input_reports,
            report_list,
            &crashes,
            handler,
        )
        .await?;
    }

    if let Some(inputs) = &inputs {
        handle_inputs(&inputs, &heartbeat_client, handler).await?;
    }

    Ok(())
}

/// Run the regression on the files in the 'inputs' location
/// * `heartbeat_client` - heartbeat client
/// * `inputs` - location of the input files
/// * `handler` - regression handler
pub async fn handle_inputs(
    inputs: &SyncedDir,
    heartbeat_client: &Option<TaskHeartbeatClient>,
    handler: &impl RegressionHandler,
) -> Result<()> {
    inputs.sync_pull().await?;
    let mut input_files = tokio::fs::read_dir(&inputs.path).await?;
    while let Some(file) = input_files.next_entry().await? {
        heartbeat_client.alive();
        let input_url = inputs.url.clone().and_then(|container_url| {
            let os_file_name = file.file_name();
            let file_name = os_file_name.to_str()?;
            container_url.url().join(file_name).ok()
        });

        let report = handler.get_crash_result(file.path(), input_url).await?;
        handler.save_regression(report, None).await?;
    }

    Ok(())
}


/// Run the regression on the reports in the 'inputs_reports' location
/// * `heartbeat_client` - heartbeat client
/// * `input_reports` - location of the reports used in this regression run
/// * `report_list` - list of report file names selected to be used in the regression
/// * `crashes` - location of the crash files referenced by the reports in input_reports
pub async fn handle_crash_reports(
    heartbeat_client: &Option<TaskHeartbeatClient>,
    input_reports: &SyncedDir,
    report_list: &[String],
    crashes: &SyncedDir,
    handler: &impl RegressionHandler,
) -> Result<()> {
    if report_list.is_empty() {
        input_reports.sync_pull().await?;
    } else {
        for file in report_list {
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
        let input_url = crash_report.input_blob.clone().and_then(|input_blob| {
            let crashes_url = crashes.url.clone()?;
            Some(crashes_url.blob(input_blob.name).url())
        });
        if let Some(input_url) = input_url {
            let input = download_input(input_url.clone(), &crashes.path).await?;
            let report = handler.get_crash_result(input, Some(input_url)).await?;

            handler.save_regression(report, Some(crash_report)).await?;
        }
    }
    Ok(())
}
