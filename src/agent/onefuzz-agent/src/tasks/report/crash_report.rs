// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use futures::StreamExt;
use onefuzz::{
    asan::AsanLog,
    blob::{BlobClient, BlobUrl},
    fs::exists,
    monitor::DirectoryMonitor,
    syncdir::SyncedDir,
};
use onefuzz_telemetry::{
    Event::{new_report, new_unable_to_reproduce, new_unique_report},
    EventData,
};
use reqwest::{StatusCode, Url};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use tokio::fs;
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize)]
pub struct CrashReport {
    pub input_sha256: String,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub input_blob: Option<InputBlob>,

    pub executable: PathBuf,

    pub crash_type: String,

    pub crash_site: String,

    pub call_stack: Vec<String>,

    pub call_stack_sha256: String,

    pub asan_log: Option<String>,

    pub task_id: Uuid,

    pub job_id: Uuid,

    pub scariness_score: Option<u32>,
    pub scariness_description: Option<String>,
}

#[derive(Debug, Deserialize, Serialize)]
pub struct NoCrash {
    pub input_sha256: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub input_blob: Option<InputBlob>,
    pub executable: PathBuf,
    pub task_id: Uuid,
    pub job_id: Uuid,
    pub tries: u64,
    pub error: Option<String>,
}

#[derive(Debug, Deserialize, Serialize)]
pub enum CrashTestResult {
    CrashReport(CrashReport),
    NoRepro(NoCrash),
}

// Conditionally upload a report, if it would not be a duplicate.
async fn _upload<T: Serialize>(report: &T, url: Url) -> Result<bool> {
    let blob = BlobClient::new();
    let result = blob
        .put(url)
        .json(report)
        // Conditional PUT, only if-not-exists.
        // https://docs.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations
        .header("If-None-Match", "*")
        .send_retry_default()
        .await?;
    Ok(result.status() == StatusCode::CREATED)
}

async fn upload_or_save_local<T: Serialize>(
    report: &T,
    dest_name: &str,
    container: &SyncedDir,
) -> Result<bool> {
    let path = container.path.join(dest_name);
    if !exists(&path).await? {
        let data = serde_json::to_vec(&report)?;
        fs::write(path, data).await?;
        container.sync_push().await?;
        Ok(true)
    } else {
        Ok(false)
    }
}

impl CrashTestResult {
    pub async fn save(
        &self,
        unique_reports: &Option<SyncedDir>,
        reports: &Option<SyncedDir>,
        no_repro: &Option<SyncedDir>,
    ) -> Result<()> {
        match self {
            Self::CrashReport(report) => {
                // Use SHA-256 of call stack as dedupe key.
                if let Some(unique_reports) = unique_reports {
                    let name = report.unique_blob_name();
                    if upload_or_save_local(&report, &name, unique_reports).await? {
                        event!(new_unique_report; EventData::Path = name);
                    }
                }

                if let Some(reports) = reports {
                    let name = report.blob_name();
                    if upload_or_save_local(&report, &name, reports).await? {
                        event!(new_report; EventData::Path = name);
                    }
                }
            }

            Self::NoRepro(report) => {
                if let Some(no_repro) = no_repro {
                    let name = report.blob_name();
                    if upload_or_save_local(&report, &name, no_repro).await? {
                        event!(new_unable_to_reproduce; EventData::Path = name);
                    }
                }
            }
        }
        Ok(())
    }
}

#[derive(Debug, Deserialize, Serialize)]
pub struct InputBlob {
    pub account: String,
    pub container: String,
    pub name: String,
}

impl From<BlobUrl> for InputBlob {
    fn from(blob: BlobUrl) -> Self {
        Self {
            account: blob.account(),
            container: blob.container(),
            name: blob.name(),
        }
    }
}

impl CrashReport {
    pub fn new(
        asan_log: AsanLog,
        task_id: Uuid,
        job_id: Uuid,
        executable: impl Into<PathBuf>,
        input_blob: Option<InputBlob>,
        input_sha256: String,
    ) -> Self {
        Self {
            input_sha256,
            input_blob,
            executable: executable.into(),
            crash_type: asan_log.fault_type().into(),
            crash_site: asan_log.summary().into(),
            call_stack: asan_log.call_stack().to_vec(),
            call_stack_sha256: asan_log.call_stack_sha256(),
            asan_log: Some(asan_log.text().to_string()),
            scariness_score: asan_log.scariness_score(),
            scariness_description: asan_log.scariness_description().to_owned(),
            task_id,
            job_id,
        }
    }

    pub fn blob_name(&self) -> String {
        format!("{}.json", self.input_sha256)
    }

    pub fn unique_blob_name(&self) -> String {
        format!("{}.json", self.call_stack_sha256)
    }
}

impl NoCrash {
    pub fn blob_name(&self) -> String {
        format!("{}.json", self.input_sha256)
    }
}

async fn parse_report_file(path: PathBuf) -> Result<CrashTestResult> {
    let raw = std::fs::read_to_string(&path)
        .with_context(|| format_err!("unable to open crash report: {}", path.display()))?;

    let json: serde_json::Value = serde_json::from_str(&raw)
        .with_context(|| format_err!("invalid json: {} - {:?}", path.display(), raw))?;

    let report: Result<CrashReport, serde_json::Error> = serde_json::from_value(json.clone());
    if let Ok(report) = report {
        return Ok(CrashTestResult::CrashReport(report));
    }
    let no_repro: Result<NoCrash, serde_json::Error> = serde_json::from_value(json);
    if let Ok(no_repro) = no_repro {
        return Ok(CrashTestResult::NoRepro(no_repro));
    }

    bail!("unable to parse report: {} - {:?}", path.display(), raw)
}

pub async fn monitor_reports(
    base_dir: &Path,
    unique_reports: &Option<SyncedDir>,
    reports: &Option<SyncedDir>,
    no_crash: &Option<SyncedDir>,
) -> Result<()> {
    if unique_reports.is_none() && reports.is_none() && no_crash.is_none() {
        debug!("no report directories configured");
        return Ok(());
    }

    let mut monitor = DirectoryMonitor::new(base_dir);
    monitor.start()?;
    while let Some(file) = monitor.next().await {
        let result = parse_report_file(file).await?;
        result.save(unique_reports, reports, no_crash).await?;
    }
    Ok(())
}
