// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use onefuzz::{
    asan::AsanLog,
    blob::{BlobClient, BlobContainerUrl, BlobUrl},
    syncdir::SyncedDir,
    telemetry::Event::{new_report, new_unable_to_reproduce, new_unique_report},
};

use reqwest::StatusCode;
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize)]
pub struct CrashReport {
    pub input_sha256: String,

    pub input_blob: InputBlob,

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
    pub input_blob: InputBlob,
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
//
// Use SHA-256 of call stack as dedupe key.
async fn upload_deduped(report: &CrashReport, container: &BlobContainerUrl) -> Result<()> {
    let blob = BlobClient::new();
    let deduped_name = report.unique_blob_name();
    let deduped_url = container.blob(deduped_name).url();
    let result = blob
        .put(deduped_url)
        .json(report)
        // Conditional PUT, only if-not-exists.
        .header("If-None-Match", "*")
        .send_retry_default()
        .await?;
    if result.status() != StatusCode::NOT_MODIFIED {
        event!(new_unique_report;);
    }
    Ok(())
}

async fn upload_report(report: &CrashReport, container: &BlobContainerUrl) -> Result<()> {
    event!(new_report;);
    let blob = BlobClient::new();
    let url = container.blob(report.blob_name()).url();
    blob.put(url).json(report).send_retry_default().await?;
    Ok(())
}

async fn upload_no_repro(report: &NoCrash, container: &BlobContainerUrl) -> Result<()> {
    event!(new_unable_to_reproduce;);
    let blob = BlobClient::new();
    let url = container.blob(report.blob_name()).url();
    blob.put(url).json(report).send_retry_default().await?;
    Ok(())
}

impl CrashTestResult {
    pub async fn upload(
        &self,
        unique_reports: &SyncedDir,
        reports: &Option<SyncedDir>,
        no_repro: &Option<SyncedDir>,
    ) -> Result<()> {
        match self {
            Self::CrashReport(report) => {
                upload_deduped(report, &unique_reports.url).await?;
                if let Some(reports) = reports {
                    upload_report(report, &reports.url).await?;
                }
            }
            Self::NoRepro(report) => {
                if let Some(no_repro) = no_repro {
                    upload_no_repro(report, &no_repro.url).await?;
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
        input_blob: InputBlob,
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
