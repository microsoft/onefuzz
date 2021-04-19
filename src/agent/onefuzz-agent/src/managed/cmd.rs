// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::config::{CommonConfig, Config};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();
    let config_path = value_t!(args, "config", PathBuf)?;
    let setup_dir = value_t!(args, "setup_dir", PathBuf)?;
    let config = Config::from_file(config_path, setup_dir)?;

    init_telemetry(config.common());
    let result = config.run().await;

    if let Err(err) = &result {
        error!("error running task: {:?}", err);
    }

    onefuzz_telemetry::try_flush_and_close();
    result
}

fn init_telemetry(config: &CommonConfig) {
    onefuzz_telemetry::set_appinsights_clients(
        config.instance_telemetry_key.clone(),
        config.microsoft_telemetry_key.clone(),
    );
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("managed fuzzing")
        .arg(Arg::with_name("config").required(true))
        .arg(Arg::with_name("setup_dir").required(true))
}
