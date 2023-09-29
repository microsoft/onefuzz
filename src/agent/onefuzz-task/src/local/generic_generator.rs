// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::tasks::{config::CommonConfig, utils::default_bool_true};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use schemars::JsonSchema;

use super::template::{RunContext, Template};

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct Generator {
    generator_exe: String,
    generator_env: HashMap<String, String>,
    generator_options: Vec<String>,
    readonly_inputs: Vec<PathBuf>,
    crashes: PathBuf,
    tools: Option<PathBuf>,

    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_timeout: Option<u64>,
    #[serde(default)]
    check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    check_debugger: bool,
    #[serde(default)]
    check_retry_count: u64,
    rename_output: bool,
    ensemble_sync_delay: Option<u64>,
}

#[async_trait]
impl Template<Generator> for Generator {
    fn example_values() -> Generator {
        Generator {
            generator_exe: String::new(),
            generator_env: HashMap::new(),
            generator_options: vec![],
            readonly_inputs: vec![PathBuf::from("path_to_readonly_inputs")],
            crashes: PathBuf::new(),
            tools: None,
            target_exe: PathBuf::from("path_to_your_exe"),
            target_env: HashMap::new(),
            target_options: vec![],
            target_timeout: None,
            check_asan_log: true,
            check_debugger: true,
            check_retry_count: 5,
            rename_output: false,
            ensemble_sync_delay: None,
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let generator_config = crate::tasks::fuzz::generator::Config {
            generator_exe: self.generator_exe.clone(),
            generator_env: self.generator_env.clone(),
            generator_options: self.generator_options.clone(),

            readonly_inputs: self
                .readonly_inputs
                .iter()
                .enumerate()
                .map(|(index, roi_pb)| {
                    context.to_monitored_sync_dir(format!("read_only_inputs_{index}"), roi_pb)
                })
                .collect::<Result<Vec<SyncedDir>>>()?,
            crashes: context.to_monitored_sync_dir("crashes", self.crashes.clone())?,
            tools: self
                .tools
                .as_ref()
                .and_then(|path_buf| context.to_monitored_sync_dir("tools", path_buf).ok()),

            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_timeout: self.target_timeout,

            check_asan_log: self.check_asan_log,
            check_debugger: self.check_debugger,
            check_retry_count: self.check_retry_count,

            rename_output: self.rename_output,
            ensemble_sync_delay: self.ensemble_sync_delay,
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let generator = crate::tasks::fuzz::generator::GeneratorTask::new(generator_config);
                generator.run().await
            })
            .await;
        Ok(())
    }
}
