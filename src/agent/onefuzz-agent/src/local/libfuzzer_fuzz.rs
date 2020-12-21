// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_target_cmd_options, build_common_config, get_target_env, CRASHES_DIR, INPUTS_DIR,
        TARGET_WORKERS,
    },
    tasks::fuzz::libfuzzer_fuzz::{Config, LibFuzzerFuzzTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub fn build_fuzz_config(args: &clap::ArgMatches<'_>) -> Result<Config> {
    let crashes = value_t!(args, CRASHES_DIR, PathBuf)?.into();
    let inputs = value_t!(args, INPUTS_DIR, PathBuf)?.into();
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let target_workers = value_t!(args, "target_workers", u64).unwrap_or_default();
    let target_env = get_target_env(args)?;
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

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app =
        SubCommand::with_name(name).about("execute a local-only libfuzzer crash report task");

    app = add_target_cmd_options(true, true, true, app);
    app.arg(Arg::with_name(INPUTS_DIR).takes_value(true).required(true))
        .arg(Arg::with_name(CRASHES_DIR).takes_value(true).required(true))
        .arg(
            Arg::with_name(TARGET_WORKERS)
                .long(TARGET_WORKERS)
                .takes_value(true),
        )
}
