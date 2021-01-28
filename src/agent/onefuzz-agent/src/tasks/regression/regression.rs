// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    heartbeat::*,
    report::crash_report::{CrashReport, CrashTestResult},
    utils::download_input,
};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use reqwest::Url;
use std::path::PathBuf;

#[async_trait]
pub trait RegressionHandler {
    async fn get_crash_result(
        &self,
        input: PathBuf,
        input_url: Option<Url>,
    ) -> Result<CrashTestResult>;
    async fn save_regression(
        &self,
        crash_result: CrashTestResult,
        original_report: Option<CrashReport>,
    ) -> Result<()>;
}

pub async fn run(
    heartbeat_client: Option<TaskHeartbeatClient>,
    input_reports: &Option<SyncedDir>,
    crashes: &Option<SyncedDir>,
    inputs: &Option<SyncedDir>,
    report_list: &[String],
    handler: &impl RegressionHandler,
) -> Result<()> {
    info!("Starting generic regression task");
    if let (Some(input_reports), Some(crashes)) = (input_reports, crashes) {
        handle_crash_reports(
            &heartbeat_client,
            &input_reports,
            &crashes,
            report_list,
            handler,
        )
        .await?;
    }

    if let Some(inputs) = &inputs {
        handle_inputs(&inputs, &heartbeat_client, handler).await?;
    }

    Ok(())
}

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
            file.file_name()
                .to_str()
                .and_then(|f_name| container_url.url().join(f_name).ok())
        });

        let report = handler.get_crash_result(file.path(), input_url).await?;
        handler.save_regression(report, None).await?;
    }

    Ok(())
}

pub async fn handle_crash_reports(
    heartbeat_client: &Option<TaskHeartbeatClient>,
    input_reports: &SyncedDir,
    crashes: &SyncedDir,
    report_list: &[String],
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
        let input_url = crash_report
            .input_blob
            .clone()
            .map(|b| b.name)
            .and_then(|crash_name| crashes.url.clone().map(|u| u.blob(crash_name).url()));

        if let Some(input_url) = input_url {
            let input = download_input(input_url.clone(), &crashes.path).await?;
            let report = handler.get_crash_result(input, Some(input_url)).await?;

            handler.save_regression(report, Some(crash_report)).await?;
        }
    }
    Ok(())
}
