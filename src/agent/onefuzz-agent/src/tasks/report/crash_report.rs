// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use futures::StreamExt;
use onefuzz::{blob::BlobUrl, monitor::DirectoryMonitor, syncdir::SyncedDir};
use onefuzz_telemetry::{
    Event::{
        new_report, new_unable_to_reproduce, new_unique_report, regression_report,
        regression_unable_to_reproduce,
    },
    EventData,
};
use serde::{Deserialize, Deserializer, Serialize};
use stacktrace_parser::CrashLog;
use std::path::{Path, PathBuf};
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize, Default)]
pub struct CrashReport {
    pub input_sha256: String,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub input_blob: Option<InputBlob>,

    pub executable: PathBuf,

    pub crash_type: String,

    pub crash_site: String,

    pub call_stack: Vec<String>,
    pub call_stack_sha256: String,

    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[serde(deserialize_with = "deserialize_null_default")]
    pub minimized_stack: Vec<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_sha256: Option<String>,

    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[serde(deserialize_with = "deserialize_null_default")]
    pub minimized_stack_function_names: Vec<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_function_names_sha256: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub asan_log: Option<String>,

    pub task_id: Uuid,

    pub job_id: Uuid,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub scariness_score: Option<u32>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub scariness_description: Option<String>,
}

fn deserialize_null_default<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    T: Default + Deserialize<'de>,
    D: Deserializer<'de>,
{
    let value = Option::deserialize(deserializer)?;
    Ok(value.unwrap_or_default())
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
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum CrashTestResult {
    CrashReport(CrashReport),
    NoRepro(NoCrash),
}

#[derive(Debug, Deserialize, Serialize)]
pub struct RegressionReport {
    pub crash_test_result: CrashTestResult,
    pub original_crash_test_result: Option<CrashTestResult>,
}

impl RegressionReport {
    pub async fn save(
        self,
        report_name: Option<String>,
        regression_reports: &SyncedDir,
    ) -> Result<()> {
        let (event, name) = match &self.crash_test_result {
            CrashTestResult::CrashReport(report) => {
                let name = report_name.unwrap_or_else(|| report.unique_blob_name());
                (regression_report, name)
            }
            CrashTestResult::NoRepro(report) => {
                let name = report_name.unwrap_or_else(|| report.blob_name());
                (regression_unable_to_reproduce, name)
            }
        };

        if upload_or_save_local(&self, &name, regression_reports).await? {
            event!(event; EventData::Path = name);
        }
        Ok(())
    }
}

async fn upload_or_save_local<T: Serialize>(
    report: &T,
    dest_name: &str,
    container: &SyncedDir,
) -> Result<bool> {
    container.upload(dest_name, report).await
}

impl CrashTestResult {
    ///  Saves the crash result as a crash report
    /// * `unique_reports` - location to save the deduplicated report if the bug was reproduced
    /// * `reports` - location to save the report if the bug was reproduced
    /// * `no_repro` - location to save the report if the bug was not reproduced
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

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct InputBlob {
    pub account: Option<String>,
    pub container: Option<String>,
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
        crash_log: CrashLog,
        task_id: Uuid,
        job_id: Uuid,
        executable: impl Into<PathBuf>,
        input_blob: Option<InputBlob>,
        input_sha256: String,
        minimized_stack_depth: Option<usize>,
    ) -> Self {
        let call_stack_sha256 = crash_log.call_stack_sha256();
        let minimized_stack_sha256 = if crash_log.minimized_stack.is_empty() {
            None
        } else {
            Some(crash_log.minimized_stack_sha256(minimized_stack_depth))
        };

        let minimized_stack_function_names_sha256 =
            if crash_log.minimized_stack_function_names.is_empty() {
                None
            } else {
                Some(crash_log.minimized_stack_function_names_sha256(minimized_stack_depth))
            };
        Self {
            input_sha256,
            input_blob,
            executable: executable.into(),
            crash_type: crash_log.fault_type,
            crash_site: crash_log.summary,
            call_stack_sha256,
            minimized_stack: crash_log.minimized_stack,
            minimized_stack_sha256,
            minimized_stack_function_names: crash_log.minimized_stack_function_names,
            minimized_stack_function_names_sha256,
            call_stack: crash_log.call_stack,
            asan_log: crash_log.text,
            scariness_score: crash_log.scariness_score,
            scariness_description: crash_log.scariness_description,
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

pub async fn parse_report_file(path: PathBuf) -> Result<CrashTestResult> {
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

#[cfg(test)]
mod tests {

    use std::io::Write;

    use super::{parse_report_file, CrashTestResult};
    use anyhow::Result;
    use tempfile::NamedTempFile;

    #[tokio::test]
    async fn test_parse_debug_report() -> Result<()> {
        let json_str = br###"
            {
                "input_url": null,
                "input_blob": {
                    "account": "fuzz27ee6imdmr5gy",
                    "container": "oft-crashes-cecbd958a1f257688f9768edaaf6c94d",
                    "name": "fake-crash-sample"
                },
                "executable": "fuzz.exe",
                "crash_type": "fake crash report",
                "crash_site": "fake crash site",
                "call_stack": ["#0 fake", "#1 call", "#2 stack"],
                "call_stack_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                "asan_log": "fake asan log",
                "task_id": "91fb6329-0aa2-4c09-afaf-26340286a9c6",
                "job_id": "447c2a03-5216-42ea-88e5-16cbb5dc6fc0",
                "scariness_score": null,
                "scariness_description": null,
                "minimized_stack": null,
                "minimized_stack_sha256": null,
                "minimized_stack_function_names": null,
                "minimized_stack_function_names_sha256": null
            }"###;

        let json_file = NamedTempFile::new()?;

        json_file.as_file().write_all(json_str)?;

        let report = parse_report_file(json_file.path().to_path_buf()).await?;

        match report {
            CrashTestResult::CrashReport(report) => {
                assert_eq!(report.minimized_stack_function_names.len(), 0);
                assert_eq!(report.minimized_stack.len(), 0);
            }
            CrashTestResult::NoRepro(norepro) => assert!(false, "invalid report type"),
        }

        Ok(())
    }
}
