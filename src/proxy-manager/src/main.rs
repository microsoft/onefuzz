// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate onefuzz_telemetry;
#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate clap;

mod config;
mod proxy;

use anyhow::Result;
use clap::{App, Arg, SubCommand};
use config::{Config, ProxyError::MissingArg};
use std::{
    io::{stdout, Write},
    time::Instant,
};
use tokio::{
    runtime::Runtime,
    time::{sleep, Duration},
};

const MINIMUM_NOTIFY_INTERVAL: Duration = Duration::from_secs(120);
const POLL_INTERVAL: Duration = Duration::from_secs(5);

async fn run_loop(mut proxy_config: Config) -> Result<()> {
    let mut last_notified = Instant::now();
    loop {
        info!("checking updates");
        proxy_config.update().await?;

        if last_notified + MINIMUM_NOTIFY_INTERVAL < Instant::now() {
            proxy_config.notify().await?;
            last_notified = Instant::now();
        }

        sleep(POLL_INTERVAL).await;
    }
}

async fn run(proxy_config: Config) -> Result<()> {
    let result = run_loop(proxy_config).await;
    if let Err(err) = &result {
        error!("run loop failed: {:?}", err);
    }
    onefuzz_telemetry::try_flush_and_close();
    result
}

fn main() -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();

    let license_cmd = SubCommand::with_name("licenses").about("display third-party licenses");

    let version = format!(
        "{} onefuzz:{} git:{}",
        crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );

    let app = App::new("onefuzz-proxy")
        .version(version.as_str())
        .arg(
            Arg::with_name("config")
                .long("config")
                .short("c")
                .takes_value(true),
        )
        .subcommand(license_cmd);
    let matches = app.get_matches();

    if matches.subcommand_matches("licenses").is_some() {
        stdout().write_all(include_bytes!("../data/licenses.json"))?;
        return Ok(());
    }

    let config_path = matches
        .value_of("config")
        .ok_or_else(|| MissingArg("--config".to_string()))?
        .parse()?;
    let proxy = Config::from_file(config_path)?;

    info!("parsed initial config");
    let rt = Runtime::new()?;
    rt.block_on(run(proxy))
}
