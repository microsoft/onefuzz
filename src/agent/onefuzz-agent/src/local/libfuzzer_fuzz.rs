// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType,
        CRASHES_DIR, INPUTS_DIR, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_WORKERS,
    },
    tasks::fuzz::libfuzzer_fuzz::{Config, LibFuzzerFuzzTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub fn build_fuzz_config(args: &clap::ArgMatches<'_>) -> Result<Config> {
    let crashes = value_t!(args, CRASHES_DIR, PathBuf)?.into();
    let inputs = value_t!(args, INPUTS_DIR, PathBuf)?.into();

    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let target_workers = value_t!(args, "target_workers", u64).unwrap_or_default();
    let readonly_inputs = None;

    let ensemble_sync_delay = None;
    let common = build_common_config(args)?;
    let config = Config {
        inputs,
        readonly_inputs,
        crashes,
        target_exe,
        target_env,
        target_options,
        target_workers,
        ensemble_sync_delay,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config = build_fuzz_config(args)?;
    LibFuzzerFuzzTask::new(config)?.local_run().await
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
            .multiple(true)
            .allow_hyphen_values(true)
            .help("Supports hyphens.  Recommendation: Set last"),
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
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer fuzzing task")
        .args(&build_shared_args())
}
