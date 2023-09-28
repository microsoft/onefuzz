// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::tasks::{config::CommonConfig, utils::default_bool_true};
use anyhow::Result;
use async_trait::async_trait;
use schemars::JsonSchema;

use super::template::{RunContext, Template};

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerRegression {
    target_exe: PathBuf,

    #[serde(default)]
    target_options: Vec<String>,

    #[serde(default)]
    target_env: HashMap<String, String>,

    target_timeout: Option<u64>,

    crashes: PathBuf,
    regression_reports: PathBuf,
    report_list: Option<Vec<String>>,
    unique_reports: Option<PathBuf>,
    reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,
    readonly_inputs: Option<PathBuf>,

    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    check_retry_count: u64,

    #[serde(default)]
    minimized_stack_depth: Option<usize>,
}

#[async_trait]
impl Template<LibfuzzerRegression> for LibfuzzerRegression {
    fn example_values() -> LibfuzzerRegression {
        LibfuzzerRegression {
            target_exe: PathBuf::from("path_to_your_exe"),
            target_options: vec![],
            target_env: HashMap::new(),
            target_timeout: None,
            crashes: PathBuf::new(),
            regression_reports: PathBuf::new(),
            report_list: None,
            unique_reports: Some(PathBuf::from("path_where_reports_written")),
            reports: Some(PathBuf::from("path_where_reports_written")),
            no_repro: Some(PathBuf::from("path_where_no_repro_reports_written")),
            readonly_inputs: None,
            check_fuzzer_help: true,
            check_retry_count: 5,
            minimized_stack_depth: None,
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let libfuzzer_regression = crate::tasks::regression::libfuzzer::Config {
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_timeout: self.target_timeout,
            crashes: context.to_monitored_sync_dir("crashes", self.crashes.clone())?,
            regression_reports: context
                .to_monitored_sync_dir("regression_reports", self.regression_reports.clone())?,
            report_list: self.report_list.clone(),

            unique_reports: self
                .unique_reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("unique_reports", c))
                .transpose()?,
            reports: self
                .reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("reports", c))
                .transpose()?,
            no_repro: self
                .no_repro
                .clone()
                .map(|c| context.to_monitored_sync_dir("no_repro", c))
                .transpose()?,
            readonly_inputs: self
                .readonly_inputs
                .clone()
                .map(|c| context.to_monitored_sync_dir("readonly_inputs", c))
                .transpose()?,

            check_fuzzer_help: self.check_fuzzer_help,
            check_retry_count: self.check_retry_count,
            minimized_stack_depth: self.minimized_stack_depth,

            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };
        context
            .spawn(async move {
                let regression = crate::tasks::regression::libfuzzer::LibFuzzerRegressionTask::new(
                    libfuzzer_regression,
                );
                regression.run().await
            })
            .await;
        Ok(())
    }
}
