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
use clap::{App, Arg, SubCommand};
use onefuzz::telemetry::{self};

mod debug;
mod tasks;

use tasks::config::Config;

fn main() -> Result<()> {
    env_logger::init();

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
        .arg(
            Arg::with_name("setup_dir")
                .long("setup_dir")
                .short("sd")
                .takes_value(true),
        )
        .subcommand(debug::cmd::args())
        .subcommand(SubCommand::with_name("licenses").about("display third-party licenses"));

    let matches = app.get_matches();

    match matches.subcommand() {
        ("licenses", Some(_)) => {
            return licenses();
        }
        ("debug", Some(sub)) => return crate::debug::cmd::run(sub),
        _ => {} // no subcommand
    }

    if matches.value_of("config").is_none() {
        println!("Missing '--config'\n{}", matches.usage());
        return Ok(());
    }

    let config_path: PathBuf = matches.value_of("config").unwrap().parse()?;
    let setup_dir = matches.value_of("setup_dir");
    let config = Config::from_file(config_path, setup_dir)?;

    init_telemetry(&config);

    verbose!("config parsed");

    let mut rt = tokio::runtime::Runtime::new()?;

    let result = rt.block_on(config.run());

    if let Err(err) = &result {
        error!("error running task: {}", err);
    }

    telemetry::try_flush_and_close();

    result
}

fn licenses() -> Result<()> {
    use std::io::{self, Write};
    io::stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn init_telemetry(config: &Config) {
    let inst_key = config.common().instrumentation_key;
    let tele_key = config.common().telemetry_key;
    telemetry::set_appinsights_clients(inst_key, tele_key);
}
