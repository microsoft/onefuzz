// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use onefuzz::{
    asan::AsanLog,
    blob::{BlobClient, BlobUrl},
    fs::exists,
    syncdir::SyncedDir,
    telemetry::{
        Event::{new_report, new_unable_to_reproduce, new_unique_report},
        EventData,
    },
};
use reqwest::{StatusCode, Url};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
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
async fn upload<T: Serialize>(report: &T, url: Url) -> Result<bool> {
    let blob = BlobClient::new();
    let result = blob
        .put(url)
        .json(report)
        // Conditional PUT, only if-not-exists.
        .header("If-None-Match", "*")
        .send_retry_default()
        .await?;
    Ok(result.status() != StatusCode::NOT_MODIFIED)
}

async fn upload_or_save_local<T: Serialize>(
    report: &T,
    dest_name: &str,
    container: &SyncedDir,
) -> Result<bool> {
    match &container.url {
        Some(blob_url) => {
            let url = blob_url.blob(dest_name).url();
            upload(report, url).await
        }
        None => {
            let path = container.path.join(dest_name);
            if !exists(&path).await? {
                let data = serde_json::to_vec(&report)?;
                fs::write(path, data).await?;
                Ok(true)
            } else {
                Ok(false)
            }
        }
    }
}

impl CrashTestResult {
    pub async fn save(
        &self,
        unique_reports: &SyncedDir,
        reports: &Option<SyncedDir>,
        no_repro: &Option<SyncedDir>,
    ) -> Result<()> {
        match self {
            Self::CrashReport(report) => {
                // Use SHA-256 of call stack as dedupe key.
                let name = report.unique_blob_name();
                if upload_or_save_local(&report, &name, unique_reports).await? {
                    event!(new_unique_report; EventData::Path = name);
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
