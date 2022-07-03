// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
use clap::{App, Arg, SubCommand};
use flume::Sender;

pub fn build_fuzz_config(
    args: &clap::ArgMatches<'_>,
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

    let rename_output = args.is_present(RENAME_OUTPUT);
    let check_asan_log = args.is_present(CHECK_ASAN_LOG);
    let check_debugger = !args.is_present(DISABLE_CHECK_DEBUGGER);
    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;
    let target_timeout = Some(value_t!(args, TARGET_TIMEOUT, u64)?);

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

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone())?;
    let config = build_fuzz_config(args, context.common_config.clone(), event_sender)?;
    GeneratorTask::new(config).run().await
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
            .default_value("{input}")
            .long(TARGET_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(GENERATOR_EXE)
            .long(GENERATOR_EXE)
            .default_value("radamsa")
            .takes_value(true)
            .required(true),
        Arg::with_name(GENERATOR_ENV)
            .long(GENERATOR_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(GENERATOR_OPTIONS)
            .long(GENERATOR_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .default_value("-H sha256 -o {generated_inputs}/input-%h.%s -n 100 -r {input_corpus}")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(CRASHES_DIR)
            .takes_value(true)
            .required(true)
            .long(CRASHES_DIR),
        Arg::with_name(READONLY_INPUTS)
            .takes_value(true)
            .required(true)
            .multiple(true)
            .long(READONLY_INPUTS),
        Arg::with_name(TOOLS_DIR).takes_value(true).long(TOOLS_DIR),
        Arg::with_name(CHECK_RETRY_COUNT)
            .takes_value(true)
            .long(CHECK_RETRY_COUNT)
            .default_value("0"),
        Arg::with_name(CHECK_ASAN_LOG)
            .takes_value(false)
            .long(CHECK_ASAN_LOG),
        Arg::with_name(RENAME_OUTPUT)
            .takes_value(false)
            .long(RENAME_OUTPUT),
        Arg::with_name(TARGET_TIMEOUT)
            .takes_value(true)
            .long(TARGET_TIMEOUT)
            .default_value("30"),
        Arg::with_name(DISABLE_CHECK_DEBUGGER)
            .takes_value(false)
            .long(DISABLE_CHECK_DEBUGGER),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only generator fuzzing task")
        .args(&build_shared_args())
}
