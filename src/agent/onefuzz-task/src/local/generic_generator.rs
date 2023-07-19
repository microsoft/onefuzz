// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir,
        get_synced_dirs, CmdType, SyncCountDirMonitor, UiEvent, CHECK_ASAN_LOG, CHECK_RETRY_COUNT,
        CRASHES_DIR, DISABLE_CHECK_DEBUGGER, GENERATOR_ENV, GENERATOR_EXE, GENERATOR_OPTIONS,
        READONLY_INPUTS, RENAME_OUTPUT, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT,
        TOOLS_DIR,
    },
    tasks::{
        config::CommonConfig,
        fuzz::generator::{Config, GeneratorTask},
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;

pub fn build_fuzz_config(
    args: &clap::ArgMatches,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_options = get_cmd_arg(CmdType::Target, args);
    let target_env = get_cmd_env(CmdType::Target, args)?;

    let generator_exe = get_cmd_exe(CmdType::Generator, args)?;
    let generator_options = get_cmd_arg(CmdType::Generator, args);
    let generator_env = get_cmd_env(CmdType::Generator, args)?;
    let readonly_inputs = get_synced_dirs(READONLY_INPUTS, common.job_id, common.task_id, args)?
        .into_iter()
        .map(|sd| sd.monitor_count(&event_sender))
        .collect::<Result<Vec<_>>>()?;

    let rename_output = args.get_flag(RENAME_OUTPUT);
    let check_asan_log = args.get_flag(CHECK_ASAN_LOG);
    let check_debugger = !args.get_flag(DISABLE_CHECK_DEBUGGER);

    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has a default");

    let target_timeout = Some(
        args.get_one::<u64>(TARGET_TIMEOUT)
            .copied()
            .expect("has a default"),
    );

    let tools = get_synced_dir(TOOLS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let ensemble_sync_delay = None;

    let config = Config {
        generator_exe,
        generator_env,
        generator_options,
        readonly_inputs,
        crashes,
        tools,
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_asan_log,
        check_debugger,
        check_retry_count,
        rename_output,
        ensemble_sync_delay,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_fuzz_config(args, context.common_config.clone(), event_sender)?;
    GeneratorTask::new(config).run().await
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .default_value("{input}")
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(GENERATOR_EXE)
            .long(GENERATOR_EXE)
            .default_value("radamsa")
            .required(true),
        Arg::new(GENERATOR_ENV).long(GENERATOR_ENV).num_args(0..),
        Arg::new(GENERATOR_OPTIONS)
            .long(GENERATOR_OPTIONS)
            .value_delimiter(' ')
            .default_value("-H sha256 -o {generated_inputs}/input-%h.%s -n 100 -r {input_corpus}")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(CRASHES_DIR)
            .required(true)
            .long(CRASHES_DIR)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(READONLY_INPUTS)
            .required(true)
            .num_args(1..)
            .value_parser(value_parser!(PathBuf))
            .long(READONLY_INPUTS),
        Arg::new(TOOLS_DIR)
            .long(TOOLS_DIR)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(CHECK_RETRY_COUNT)
            .long(CHECK_RETRY_COUNT)
            .value_parser(value_parser!(u64))
            .default_value("0"),
        Arg::new(CHECK_ASAN_LOG)
            .action(ArgAction::SetTrue)
            .long(CHECK_ASAN_LOG),
        Arg::new(RENAME_OUTPUT)
            .action(ArgAction::SetTrue)
            .long(RENAME_OUTPUT),
        Arg::new(TARGET_TIMEOUT)
            .long(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64))
            .default_value("30"),
        Arg::new(DISABLE_CHECK_DEBUGGER)
            .action(ArgAction::SetTrue)
            .long(DISABLE_CHECK_DEBUGGER),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only generator fuzzing task")
        .args(&build_shared_args())
}
