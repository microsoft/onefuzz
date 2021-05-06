// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
use clap::{App, Arg, SubCommand};
use flume::Sender;
use storage_queue::QueueClient;

pub fn build_merge_config(
    args: &clap::ArgMatches<'_>,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let check_fuzzer_help = args.is_present(CHECK_FUZZER_HELP);
    let inputs = get_synced_dirs(ANALYSIS_INPUTS, common.job_id, common.task_id, args)?
        .into_iter()
        .map(|sd| sd.monitor_count(&event_sender))
        .collect::<Result<Vec<_>>>()?;
    let unique_inputs =
        get_synced_dir(ANALYSIS_UNIQUE_INPUTS, common.job_id, common.task_id, args)?
            .monitor_count(&event_sender)?;
    let preserve_existing_outputs = value_t!(args, PRESERVE_EXISTING_OUTPUTS, bool)?;

    let config = Config {
        target_exe,
        target_env,
        target_options,
        check_fuzzer_help,
        input_queue,
        common,
        inputs,
        unique_inputs,
        preserve_existing_outputs,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone())?;
    let config = build_merge_config(args, None, context.common_config.clone(), event_sender)?;
    spawn(std::sync::Arc::new(config)).await
}

pub fn build_shared_args() -> Vec<Arg<'static, 'static>> {
    vec![
        Arg::with_name(TARGET_EXE)
            .long(TARGET_EXE)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_ENV)
            .long(TARGET_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(CHECK_FUZZER_HELP)
            .takes_value(false)
            .long(CHECK_FUZZER_HELP),
        Arg::with_name(INPUTS_DIR)
            .long(INPUTS_DIR)
            .takes_value(true)
            .multiple(true),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer crash report task")
        .args(&build_shared_args())
}
