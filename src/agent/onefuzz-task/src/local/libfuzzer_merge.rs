// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir,
        get_synced_dirs, CmdType, SyncCountDirMonitor, UiEvent, ANALYSIS_INPUTS,
        ANALYSIS_UNIQUE_INPUTS, CHECK_FUZZER_HELP, INPUTS_DIR, PRESERVE_EXISTING_OUTPUTS,
        TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
    },
    tasks::{
        config::CommonConfig,
        merge::libfuzzer_merge::{spawn, Config},
        utils::default_bool_true,
    },
};
use anyhow::Result;
use async_trait::async_trait;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use futures::future::OptionFuture;
use onefuzz::syncdir::SyncedDir;
use schemars::JsonSchema;
use storage_queue::QueueClient;

use super::template::{RunContext, Template};

pub fn build_merge_config(
    args: &clap::ArgMatches,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let check_fuzzer_help = args.get_flag(CHECK_FUZZER_HELP);
    let inputs = get_synced_dirs(ANALYSIS_INPUTS, common.job_id, common.task_id, args)?
        .into_iter()
        .map(|sd| sd.monitor_count(&event_sender))
        .collect::<Result<Vec<_>>>()?;
    let unique_inputs =
        get_synced_dir(ANALYSIS_UNIQUE_INPUTS, common.job_id, common.task_id, args)?
            .monitor_count(&event_sender)?;
    let preserve_existing_outputs = args
        .get_one::<bool>(PRESERVE_EXISTING_OUTPUTS)
        .copied()
        .unwrap_or_default();

    let config = Config {
        target_exe,
        target_env,
        target_options,
        input_queue,
        inputs,
        unique_inputs,
        preserve_existing_outputs,
        check_fuzzer_help,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_merge_config(args, None, context.common_config.clone(), event_sender)?;
    spawn(config).await
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
        Arg::new(INPUTS_DIR)
            .long(INPUTS_DIR)
            .value_parser(value_parser!(PathBuf))
            .num_args(0..),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only libfuzzer crash report task")
        .args(&build_shared_args())
}

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
