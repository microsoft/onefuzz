// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_target_cmd_options, build_common_config, get_target_env, TARGET_EXE, TARGET_OPTIONS,
    },
    tasks::coverage::libfuzzer_coverage::{Config, CoverageTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

const COVERAGE_DIR: &str = "coverage_dir";
const READONLY_INPUTS: &str = "readonly_inputs_dir";

pub fn build_coverage_config(args: &clap::ArgMatches<'_>) -> Result<Config> {
    let target_exe = value_t!(args, TARGET_EXE, PathBuf)?;
    let readonly_inputs = values_t!(args, READONLY_INPUTS, PathBuf)?
        .iter()
        .map(|x| x.to_owned().into())
        .collect();
    let coverage = value_t!(args, COVERAGE_DIR, PathBuf)?.into();
    let target_options = args.values_of_lossy(TARGET_OPTIONS).unwrap_or_default();

    let target_env = get_target_env(args)?;

    let common = build_common_config(args)?;
    let config = Config {
        target_exe,
        target_env,
        target_options,
        input_queue: None,
        readonly_inputs,
        coverage,
        common,
    };
    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config = build_coverage_config(args)?;

    let task = CoverageTask::new(config);
    task.local_run().await
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("execute a local-only libfuzzer coverage task");

    app = add_target_cmd_options(true, true, true, app);

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
