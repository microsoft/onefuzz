// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    fuzz::libfuzzer::{common::default_workers, generic::LibFuzzerFuzzTask},
    utils::default_bool_true,
};
use anyhow::Result;
use async_trait::async_trait;
use onefuzz::syncdir::SyncedDir;
use schemars::JsonSchema;
use std::{collections::HashMap, path::PathBuf};

use super::template::{RunContext, Template};

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibFuzzer {
    inputs: PathBuf,
    readonly_inputs: Vec<PathBuf>,
    crashes: PathBuf,
    crashdumps: Option<PathBuf>,
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_workers: Option<usize>,
    ensemble_sync_delay: Option<u64>,
    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    expect_crash_on_failure: bool,
}

#[async_trait]
impl Template<LibFuzzer> for LibFuzzer {
    fn example_values() -> LibFuzzer {
        LibFuzzer {
            inputs: PathBuf::new(),
            readonly_inputs: vec![PathBuf::from("path_to_readonly_inputs")],
            crashes: PathBuf::new(),
            crashdumps: None,
            target_exe: PathBuf::from("path_to_your_exe"),
            target_env: HashMap::new(),
            target_options: vec![],
            target_workers: None,
            ensemble_sync_delay: None,
            check_fuzzer_help: true,
            expect_crash_on_failure: true,
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let ri: Result<Vec<SyncedDir>> = self
            .readonly_inputs
            .iter()
            .enumerate()
            .map(|(index, input)| context.to_sync_dir(format!("readonly_inputs_{index}"), input))
            .collect();

        let libfuzzer_config = crate::tasks::fuzz::libfuzzer::generic::Config {
            inputs: context.to_monitored_sync_dir("inputs", &self.inputs)?,
            readonly_inputs: Some(ri?),
            crashes: context.to_monitored_sync_dir("crashes", &self.crashes)?,
            crashdumps: self
                .crashdumps
                .as_ref()
                .and_then(|path| context.to_monitored_sync_dir("crashdumps", path).ok()),
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_workers: self.target_workers.unwrap_or(default_workers()),
            ensemble_sync_delay: self.ensemble_sync_delay,
            check_fuzzer_help: self.check_fuzzer_help,
            expect_crash_on_failure: self.expect_crash_on_failure,
            extra: (),
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let fuzzer = LibFuzzerFuzzTask::new(libfuzzer_config)?;
                fuzzer.run().await
            })
            .await;
        Ok(())
    }
}
