// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

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
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use storage_queue::QueueClient;

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
