// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

mod config;
mod proxy;

use anyhow::Result;
use clap::{Arg, Command};
use config::Config;
use onefuzz_telemetry::{error, info};
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
    onefuzz_telemetry::try_flush_and_close().await;
    result
}

fn main() -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();

    let license_cmd = Command::new("licenses").about("display third-party licenses");

    let version = format!(
        "{} onefuzz:{} git:{}",
        clap::crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );

    let app = Command::new("onefuzz-proxy")
        .version(version)
        .arg(Arg::new("config").long("config").short('c').required(true))
        .subcommand(license_cmd);
    let matches = app.get_matches();

    if matches.subcommand_matches("licenses").is_some() {
        stdout().write_all(include_bytes!("../data/licenses.json"))?;
        return Ok(());
    }

    let config_path = matches
        .get_one::<String>("config")
        .expect("was required")
        .parse()?;

    let rt = Runtime::new()?;
    let proxy = rt.block_on(Config::from_file(config_path))?;
    info!("parsed initial config");

    rt.block_on(run(proxy))
}
