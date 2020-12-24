// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_cmd_options, build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType,
        CHECK_ASAN_LOG, CHECK_RETRY_COUNT, CRASHES_DIR, READONLY_INPUTS, RENAME_OUTPUT,
        TARGET_TIMEOUT, TOOLS_DIR,
    },
    tasks::fuzz::generator::{Config, GeneratorTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub fn build_fuzz_config(args: &clap::ArgMatches<'_>) -> Result<Config> {
    let crashes = value_t!(args, CRASHES_DIR, PathBuf)?.into();
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_options = get_cmd_arg(CmdType::Target, args);
    let target_env = get_cmd_env(CmdType::Target, args)?;

    let generator_exe = get_cmd_exe(CmdType::Generator, args)?;
    let generator_options = get_cmd_arg(CmdType::Generator, args);
    let generator_env = get_cmd_env(CmdType::Generator, args)?;

    let readonly_inputs = values_t!(args, READONLY_INPUTS, PathBuf)?
        .iter()
        .map(|x| x.to_owned().into())
        .collect();

    let rename_output = args.is_present(RENAME_OUTPUT);
    let check_asan_log = args.is_present(CHECK_ASAN_LOG);
    let check_debugger = args.is_present("disable_check_debugger");
    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;
    let target_timeout = Some(value_t!(args, TARGET_TIMEOUT, u64)?);

    let tools = if args.is_present(TOOLS_DIR) {
        Some(value_t!(args, TOOLS_DIR, PathBuf)?.into())
    } else {
        None
    };

    let ensemble_sync_delay = None;
    let common = build_common_config(args)?;
    let config = Config {
        tools,
        generator_exe,
        generator_env,
        generator_options,
        target_exe,
        target_env,
        target_options,
        target_timeout,
        readonly_inputs,
        crashes,
        ensemble_sync_delay,
        check_asan_log,
        check_debugger,
        check_retry_count,
        rename_output,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config = build_fuzz_config(args)?;
    GeneratorTask::new(config).local_run().await
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("execute a local-only generator fuzzing task");

    app = add_cmd_options(CmdType::Generator, true, true, true, app);
    app = add_cmd_options(CmdType::Target, true, true, true, app);
    app.arg(Arg::with_name(CRASHES_DIR).takes_value(true).required(true))
        .arg(
            Arg::with_name(READONLY_INPUTS)
                .takes_value(true)
                .required(true)
                .multiple(true),
        )
        .arg(Arg::with_name(TOOLS_DIR).takes_value(true))
        .arg(
            Arg::with_name(CHECK_RETRY_COUNT)
                .takes_value(true)
                .long(CHECK_RETRY_COUNT)
                .default_value("0"),
        )
        .arg(
            Arg::with_name(CHECK_ASAN_LOG)
                .takes_value(false)
                .long(CHECK_ASAN_LOG),
        )
        .arg(
            Arg::with_name(RENAME_OUTPUT)
                .takes_value(false)
                .long(RENAME_OUTPUT),
        )
        .arg(
            Arg::with_name(TARGET_TIMEOUT)
                .takes_value(true)
                .long(TARGET_TIMEOUT)
                .default_value("30"),
        )
        .arg(
            Arg::with_name("disable_check_debugger")
                .takes_value(false)
                .long("disable_check_debugger"),
        )
}
