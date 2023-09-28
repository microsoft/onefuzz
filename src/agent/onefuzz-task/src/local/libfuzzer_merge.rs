// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::tasks::{config::CommonConfig, utils::default_bool_true};
use anyhow::Result;
use async_trait::async_trait;
use futures::future::OptionFuture;
use onefuzz::syncdir::SyncedDir;
use schemars::JsonSchema;

use super::template::{RunContext, Template};

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerMerge {
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    input_queue: Option<PathBuf>,
    inputs: Vec<PathBuf>,
    unique_inputs: PathBuf,
    preserve_existing_outputs: bool,

    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
}

#[async_trait]
impl Template<LibfuzzerMerge> for LibfuzzerMerge {
    fn example_values() -> LibfuzzerMerge {
        LibfuzzerMerge {
            target_exe: PathBuf::from("path_to_your_exe"),
            target_env: HashMap::new(),
            target_options: vec![],
            input_queue: Some(PathBuf::from("path_to_your_inputs")),
            inputs: vec![],
            unique_inputs: PathBuf::new(),
            preserve_existing_outputs: true,
            check_fuzzer_help: true,
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

        let libfuzzer_merge = crate::tasks::merge::libfuzzer_merge::Config {
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            input_queue: input_q,
            inputs: self
                .inputs
                .iter()
                .enumerate()
                .map(|(index, roi_pb)| {
                    context.to_monitored_sync_dir(format!("inputs_{index}"), roi_pb)
                })
                .collect::<Result<Vec<SyncedDir>>>()?,
            unique_inputs: context
                .to_monitored_sync_dir("unique_inputs", self.unique_inputs.clone())?,
            preserve_existing_outputs: self.preserve_existing_outputs,

            check_fuzzer_help: self.check_fuzzer_help,

            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(
                async move { crate::tasks::merge::libfuzzer_merge::spawn(libfuzzer_merge).await },
            )
            .await;
        Ok(())
    }
}
