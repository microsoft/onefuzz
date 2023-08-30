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
pub struct CrashReport {
    target_exe: PathBuf,
    target_options: Vec<String>,
    target_env: HashMap<String, String>,

    input_queue: Option<PathBuf>,
    crashes: Option<PathBuf>,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,

    target_timeout: Option<u64>,

    #[serde(default)]
    check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    check_debugger: bool,
    #[serde(default)]
    check_retry_count: u64,

    #[serde(default = "default_bool_true")]
    check_queue: bool,

    #[serde(default)]
    minimized_stack_depth: Option<usize>,
}
#[async_trait]
impl Template<CrashReport> for CrashReport {
    fn example_values() -> CrashReport {
        CrashReport {
            target_exe: PathBuf::from("path_to_your_exe"),
            target_options: vec![],
            target_env: HashMap::new(),
            input_queue: Some(PathBuf::from("path_to_your_inputs")),
            crashes: Some(PathBuf::from("path_where_crashes_written")),
            reports: Some(PathBuf::from("path_where_reports_written")),
            unique_reports: Some(PathBuf::from("path_where_reports_written")),
            no_repro: Some(PathBuf::from("path_where_no_repro_reports_written")),
            target_timeout: None,
            check_asan_log: true,
            check_debugger: true,
            check_retry_count: 5,
            check_queue: false,
            minimized_stack_depth: None,
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

        let crash_report_config = crate::tasks::report::generic::Config {
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

            check_asan_log: self.check_asan_log,
            check_debugger: self.check_debugger,
            check_retry_count: self.check_retry_count,
            check_queue: self.check_queue,
            minimized_stack_depth: self.minimized_stack_depth,
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let mut report =
                    crate::tasks::report::generic::ReportTask::new(crash_report_config);
                report.managed_run().await
            })
            .await;
        Ok(())
    }
}
