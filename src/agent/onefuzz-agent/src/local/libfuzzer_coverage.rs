// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType, COVERAGE_DIR,
        INPUTS_DIR, READONLY_INPUTS, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
    },
    tasks::coverage::libfuzzer_coverage::{Config, CoverageTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub fn build_coverage_config(args: &clap::ArgMatches<'_>, local_job: bool) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let readonly_inputs = if local_job {
        vec![value_t!(args, INPUTS_DIR, PathBuf)?.into()]
    } else {
        values_t!(args, READONLY_INPUTS, PathBuf)?
            .iter()
            .map(|x| x.to_owned().into())
            .collect()
    };

    let coverage = value_t!(args, COVERAGE_DIR, PathBuf)?.into();

    let common = build_common_config(args)?;
    let config = Config {
        target_exe,
        target_env,
        target_options,
        input_queue: None,
        readonly_inputs,
        coverage,
        common,
        check_queue: false,
    };
    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config = build_coverage_config(args, false)?;

    let task = CoverageTask::new(config);
    task.local_run().await
}

pub fn build_shared_args(local_job: bool) -> Vec<Arg<'static, 'static>> {
    let mut args = vec![
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
        Arg::with_name(COVERAGE_DIR)
            .takes_value(true)
            .required(!local_job)
            .long(COVERAGE_DIR),
    ];
    if local_job {
        args.push(
            Arg::with_name(INPUTS_DIR)
                .long(INPUTS_DIR)
                .takes_value(true)
                .required(true),
        )
    } else {
        args.push(
            Arg::with_name(READONLY_INPUTS)
                .takes_value(true)
                .required(true)
                .long(READONLY_INPUTS)
                .multiple(true),
        )
    }
    args
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer coverage task")
        .args(&build_shared_args(false))
}
