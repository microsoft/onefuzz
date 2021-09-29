// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use onefuzz::{blob::BlobUrl, monitor::DirectoryMonitor, syncdir::SyncedDir};
use onefuzz_telemetry::{
    Event::{
        new_report, new_unable_to_reproduce, new_unique_report, regression_report,
        regression_unable_to_reproduce,
    },
    EventData,
};
use serde::{Deserialize, Serialize};
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

    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack: Option<Vec<String>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_sha256: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_function_names: Option<Vec<String>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_function_names_sha256: Option<String>,

    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_function_lines: Option<Vec<String>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub minimized_stack_function_lines_sha256: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub asan_log: Option<String>,

    pub task_id: Uuid,

    pub job_id: Uuid,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub scariness_score: Option<u32>,

    #[serde(skip_serializing_if = "Option::is_none")]
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

        let minimized_stack_function_lines_sha256 =
            if crash_log.minimized_stack_function_lines.is_empty() {
                None
            } else {
                Some(crash_log.minimized_stack_function_lines_sha256(minimized_stack_depth))
            };

        let minimized_stack_function_names_sha256 =
            if crash_log.minimized_stack_function_names.is_empty() {
                None
            } else {
                Some(crash_log.minimized_stack_function_names_sha256(minimized_stack_depth))
            };

        let minimized_stack_function_lines = if crash_log.minimized_stack_function_lines.is_empty()
        {
            None
        } else {
            Some(crash_log.minimized_stack_function_lines)
        };

        let minimized_stack_function_names = if crash_log.minimized_stack_function_names.is_empty()
        {
            None
        } else {
            Some(crash_log.minimized_stack_function_names)
        };

        Self {
            input_sha256,
            input_blob,
            executable: executable.into(),
            crash_type: crash_log.fault_type,
            crash_site: crash_log.summary,
            call_stack_sha256,
            minimized_stack: Some(crash_log.minimized_stack),
            minimized_stack_sha256,
            minimized_stack_function_names,
            minimized_stack_function_names_sha256,
            minimized_stack_function_lines,
            minimized_stack_function_lines_sha256,
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

    let report_err = match report {
        Ok(report) => return Ok(CrashTestResult::CrashReport(report)),
        Err(err) => err,
    };
    let no_repro: Result<NoCrash, serde_json::Error> = serde_json::from_value(json);

    let no_repro_err = match no_repro {
        Ok(no_repro) => return Ok(CrashTestResult::NoRepro(no_repro)),
        Err(err) => err,
    };

    bail!(
        "unable to parse report: {} - {:?} - report error: {:?} no_repo error: {:?}",
        path.display(),
        raw,
        report_err,
        no_repro_err
    )
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
    while let Some(file) = monitor.next_file().await {
        let result = parse_report_file(file).await?;
        result.save(unique_reports, reports, no_crash).await?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use anyhow::Result;

    #[tokio::test]
    async fn test_parse_fake_crash_report() -> Result<()> {
        let path = std::path::PathBuf::from("data/fake-crash-report.json");
        parse_report_file(path).await?;
        Ok(())
    }
}
