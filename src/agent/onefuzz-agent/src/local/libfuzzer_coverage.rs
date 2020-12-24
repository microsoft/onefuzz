// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_cmd_options, build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType,
        COVERAGE_DIR, INPUTS_DIR, READONLY_INPUTS,
    },
    tasks::coverage::libfuzzer_coverage::{Config, CoverageTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub fn build_coverage_config(args: &clap::ArgMatches<'_>, use_inputs: bool) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let readonly_inputs = if use_inputs {
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

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("execute a local-only libfuzzer coverage task");

    app = add_cmd_options(CmdType::Target, true, true, true, app);

    app.arg(
        Arg::with_name(COVERAGE_DIR)
            .takes_value(true)
            .required(true),
    )
    .arg(
        Arg::with_name(READONLY_INPUTS)
            .takes_value(true)
            .required(true)
            .multiple(true),
    )
}
