// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::config::{CommonConfig, Config};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::telemetry;
use std::path::PathBuf;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config_path = value_t!(args, "config", PathBuf)?;
    let config = Config::from_file(config_path)?;

    init_telemetry(config.common());
    let result = config.run().await;

    if let Err(err) = &result {
        error!("error running task: {}", err);
    }

    telemetry::try_flush_and_close();
    result
}

fn init_telemetry(config: &CommonConfig) {
    telemetry::set_appinsights_clients(config.instrumentation_key, config.telemetry_key);
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("managed fuzzing")
        .arg(Arg::with_name("config").required(true))
}
