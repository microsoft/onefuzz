// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::tasks::config::CommonConfig;
use anyhow::Result;
use async_trait::async_trait;
use schemars::JsonSchema;

use super::template::{RunContext, Template};

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct Analysis {
    analyzer_exe: String,
    analyzer_options: Vec<String>,
    analyzer_env: HashMap<String, String>,
    target_exe: PathBuf,
    target_options: Vec<String>,
    input_queue: Option<PathBuf>,
    crashes: Option<PathBuf>,
    analysis: PathBuf,
    tools: Option<PathBuf>,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,
}

#[async_trait]
impl Template<Analysis> for Analysis {
    fn example_values() -> Analysis {
        Analysis {
            analyzer_exe: String::new(),
            analyzer_options: vec![],
            analyzer_env: HashMap::new(),
            target_exe: PathBuf::from("path_to_your_exe"),
            target_options: vec![],
            input_queue: Some(PathBuf::from("path_to_your_inputs")),
            crashes: Some(PathBuf::from("path_where_crashes_written")),
            analysis: PathBuf::new(),
            tools: None,
            reports: Some(PathBuf::from("path_where_reports_written")),
            unique_reports: Some(PathBuf::from("path_where_reports_written")),
            no_repro: Some(PathBuf::from("path_where_no_repro_reports_written")),
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let input_q = if let Some(w) = &self.input_queue {
            Some(context.monitor_dir(w).await?)
        } else {
            None
        };

        let analysis_config = crate::tasks::analysis::generic::Config {
            analyzer_exe: self.analyzer_exe.clone(),
            analyzer_options: self.analyzer_options.clone(),
            analyzer_env: self.analyzer_env.clone(),

            target_exe: self.target_exe.clone(),
            target_options: self.target_options.clone(),
            input_queue: input_q,
            crashes: self
                .crashes
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("crashes", path).ok()),

            analysis: context.to_monitored_sync_dir("analysis", self.analysis.clone())?,
            tools: self
                .tools
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("tools", path).ok()),

            reports: self
                .reports
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("reports", path).ok()),
            unique_reports: self
                .unique_reports
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("unique_reports", path).ok()),
            no_repro: self
                .no_repro
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("no_repro", path).ok()),

            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move { crate::tasks::analysis::generic::run(analysis_config).await })
            .await;

        Ok(())
    }
}
