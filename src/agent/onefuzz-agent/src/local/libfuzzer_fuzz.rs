// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        CHECK_FUZZER_HELP, CRASHES_DIR, INPUTS_DIR, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_WORKERS,
    },
    tasks::{
        config::CommonConfig,
        fuzz::libfuzzer_fuzz::{Config, LibFuzzerFuzzTask},
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};

const DISABLE_EXPECT_CRASH_ON_FAILURE: &str = "disable_expect_crash_on_failure";

pub fn build_fuzz_config(args: &clap::ArgMatches<'_>, common: CommonConfig) -> Result<Config> {
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)?;
    let inputs = get_synced_dir(INPUTS_DIR, common.job_id, common.task_id, args)?;

    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let target_workers = value_t!(args, "target_workers", u64).unwrap_or_default();
    let readonly_inputs = None;
    let check_fuzzer_help = args.is_present(CHECK_FUZZER_HELP);
    let expect_crash_on_failure = !args.is_present(DISABLE_EXPECT_CRASH_ON_FAILURE);

    let ensemble_sync_delay = None;

    let config = Config {
        inputs,
        readonly_inputs,
        crashes,
        target_exe,
        target_env,
        target_options,
        target_workers,
        ensemble_sync_delay,
        expect_crash_on_failure,
        check_fuzzer_help,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let common = build_common_config(args, true)?;
    let config = build_fuzz_config(args, common)?;
    LibFuzzerFuzzTask::new(config)?.run().await
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
        Arg::with_name(INPUTS_DIR)
            .long(INPUTS_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(CRASHES_DIR)
            .long(CRASHES_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_WORKERS)
            .long(TARGET_WORKERS)
            .takes_value(true),
        Arg::with_name(CHECK_FUZZER_HELP)
            .takes_value(false)
            .long(CHECK_FUZZER_HELP),
        Arg::with_name(DISABLE_EXPECT_CRASH_ON_FAILURE)
            .takes_value(false)
            .long(DISABLE_EXPECT_CRASH_ON_FAILURE),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer fuzzing task")
        .args(&build_shared_args())
}
