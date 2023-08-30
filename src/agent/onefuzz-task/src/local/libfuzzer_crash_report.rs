// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::tasks::{config::CommonConfig, utils::default_bool_true};
use anyhow::Result;
use async_trait::async_trait;
use futures::future::OptionFuture;
use schemars::JsonSchema;

use super::template::{RunContext, Template};
#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerCrashReport {
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_timeout: Option<u64>,
    input_queue: Option<PathBuf>,
    crashes: Option<PathBuf>,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,

    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,

    #[serde(default)]
    check_retry_count: u64,

    #[serde(default)]
    minimized_stack_depth: Option<usize>,

    #[serde(default = "default_bool_true")]
    check_queue: bool,
}

#[async_trait]
impl Template<LibfuzzerCrashReport> for LibfuzzerCrashReport {
    fn example_values() -> LibfuzzerCrashReport {
        LibfuzzerCrashReport {
            target_exe: PathBuf::from("path_to_your_exe"),
            target_env: HashMap::new(),
            target_options: vec![],
            target_timeout: None,
            input_queue: Some(PathBuf::from("path_to_your_inputs")),
            crashes: Some(PathBuf::from("path_where_crashes_written")),
            reports: Some(PathBuf::from("path_where_reports_written")),
            unique_reports: Some(PathBuf::from("path_where_reports_written")),
            no_repro: Some(PathBuf::from("path_where_no_repro_reports_written")),
            check_fuzzer_help: true,
            check_retry_count: 5,
            minimized_stack_depth: None,
            check_queue: true,
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let input_q_fut: OptionFuture<_> = self
            .input_queue
            .iter()
            .map(|w| context.monitor_dir(w))
            .next()
            .into();
        let input_q = input_q_fut.await.transpose()?;

        let libfuzzer_crash_config = crate::tasks::report::libfuzzer_report::Config {
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_timeout: self.target_timeout,
            input_queue: input_q,
            crashes: self
                .crashes
                .clone()
                .map(|c| context.to_monitored_sync_dir("crashes", c))
                .transpose()?,
            reports: self
                .reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("reports", c))
                .transpose()?,
            unique_reports: self
                .unique_reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("unique_reports", c))
                .transpose()?,
            no_repro: self
                .no_repro
                .clone()
                .map(|c| context.to_monitored_sync_dir("no_repro", c))
                .transpose()?,

            check_fuzzer_help: self.check_fuzzer_help,
            check_retry_count: self.check_retry_count,
            minimized_stack_depth: self.minimized_stack_depth,
            check_queue: self.check_queue,
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let mut libfuzzer_report =
                    crate::tasks::report::libfuzzer_report::ReportTask::new(libfuzzer_crash_config);
                libfuzzer_report.managed_run().await
            })
            .await;
        Ok(())
    }
}
