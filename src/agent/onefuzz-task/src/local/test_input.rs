// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, CmdType, UiEvent, CHECK_ASAN_LOG,
        CHECK_RETRY_COUNT, DISABLE_CHECK_DEBUGGER, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT,
    },
    tasks::report::generic::{test_input, TestInputArgs},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use flume::Sender;
use std::path::PathBuf;

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, false, event_sender)?;

    let target_exe = value_t!(args, TARGET_EXE, PathBuf)?;
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let input = value_t!(args, "input", PathBuf)?;
    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();
    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;
    let check_asan_log = args.is_present(CHECK_ASAN_LOG);
    let check_debugger = !args.is_present(DISABLE_CHECK_DEBUGGER);

    let config = TestInputArgs {
        target_exe: target_exe.as_path(),
        target_env: &target_env,
        target_options: &target_options,
        input_url: None,
        input: input.as_path(),
        job_id: context.common_config.job_id,
        task_id: context.common_config.task_id,
        target_timeout,
        check_retry_count,
        setup_dir: &context.common_config.setup_dir,
        minimized_stack_depth: None,
        check_asan_log,
        check_debugger,
    };

    let result = test_input(config).await?;
    println!("{}", serde_json::to_string_pretty(&result)?);
    Ok(())
}

pub fn build_shared_args() -> Vec<Arg<'static, 'static>> {
    vec![
        Arg::with_name(TARGET_EXE).takes_value(true).required(true),
        Arg::with_name("input").takes_value(true).required(true),
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
        Arg::with_name(TARGET_TIMEOUT)
            .takes_value(true)
            .long(TARGET_TIMEOUT),
        Arg::with_name(CHECK_RETRY_COUNT)
            .takes_value(true)
            .long(CHECK_RETRY_COUNT)
            .default_value("0"),
        Arg::with_name(CHECK_ASAN_LOG)
            .takes_value(false)
            .long(CHECK_ASAN_LOG),
        Arg::with_name(DISABLE_CHECK_DEBUGGER)
            .takes_value(false)
            .long("disable_check_debugger"),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("test an application with a specific input")
        .args(&build_shared_args())
}
