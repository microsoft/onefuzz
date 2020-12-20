// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate anyhow;

#[macro_use]
extern crate onefuzz;

#[macro_use]
extern crate clap;

use std::path::PathBuf;

use anyhow::Result;
use clap::{App, Arg, ArgMatches, SubCommand};
use onefuzz::telemetry::{self};
use std::io::{stdout, Write};

mod debug;
mod local;
mod tasks;

use tasks::config::{CommonConfig, Config};

const LOCAL_CMD: &str = "local";
const DEBUG_CMD: &str = "debug";

fn main() -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();

    let built_version = format!(
        "{} onefuzz:{} git:{}",
        crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );

    let app = App::new("onefuzz-agent")
        .version(built_version.as_str())
        .arg(
            Arg::with_name("config")
                .long("config")
                .short("c")
                .takes_value(true),
        )
        .subcommand(local::cmd::args(LOCAL_CMD))
        .subcommand(debug::cmd::args(DEBUG_CMD))
        .subcommand(SubCommand::with_name("licenses").about("display third-party licenses"));

    let matches = app.get_matches();

    let mut rt = tokio::runtime::Runtime::new()?;
    rt.block_on(run(matches))
}

async fn run(matches: ArgMatches<'_>) -> Result<()> {
    match matches.subcommand() {
        ("licenses", Some(_)) => {
            return licenses();
        }
        (DEBUG_CMD, Some(sub)) => return debug::cmd::run(sub).await,
        (LOCAL_CMD, Some(sub)) => return local::cmd::run(sub).await,
        _ => {} // no subcommand
    }

    if matches.value_of("config").is_none() {
        println!("Missing '--config'\n{}", matches.usage());
        return Ok(());
    }

    let config_arg = matches.value_of("config").unwrap();
    run_config(config_arg).await
}

async fn run_config(config_arg: &str) -> Result<()> {
    let config_path: PathBuf = config_arg.parse()?;
    let config = Config::from_file(config_path)?;

    init_telemetry(config.common());
    verbose!("config parsed");
    let result = config.run().await;

    if let Err(err) = &result {
        error!("error running task: {}", err);
    }

    telemetry::try_flush_and_close();
    result
}

fn licenses() -> Result<()> {
    stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn init_telemetry(config: &CommonConfig) {
    telemetry::set_appinsights_clients(config.instrumentation_key, config.telemetry_key);
}
