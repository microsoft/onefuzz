// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate onefuzz;
#[macro_use]
extern crate serde;
#[macro_use]
extern crate clap;

use std::path::PathBuf;

use anyhow::Result;
use onefuzz::{
    machine_id::get_machine_id,
    telemetry::{self, EventData},
};
use structopt::StructOpt;

pub mod config;
pub mod setup;

use config::StaticConfig;

#[derive(StructOpt, Debug)]
enum Opt {
    Run(RunOpt),
    Licenses,
    Version,
}

#[derive(StructOpt, Debug)]
struct RunOpt {
    #[structopt(short, long = "--config", parse(from_os_str))]
    config_path: Option<PathBuf>,

    #[structopt(short, long = "--onefuzz_path", parse(from_os_str))]
    onefuzz_path: Option<PathBuf>,

    #[structopt(short, long)]
    start_supervisor: bool,
}

fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();

    match opt {
        Opt::Run(opt) => run(opt)?,
        Opt::Licenses => licenses()?,
        Opt::Version => versions()?,
    };

    Ok(())
}

fn versions() -> Result<()> {
    println!(
        "{} onefuzz:{} git:{}",
        crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );
    Ok(())
}

fn licenses() -> Result<()> {
    use std::io::{self, Write};
    io::stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn run(opt: RunOpt) -> Result<()> {
    // We can't send telemetry if this fails.
    let config = load_config(&opt);

    if let Err(err) = &config {
        error!("error loading supervisor agent config: {:?}", err);
    }

    let config = config?;
    init_telemetry(&config);

    let mut rt = tokio::runtime::Runtime::new()?;
    let result = rt.block_on(run_downloader(config, &opt));

    if let Err(err) = &result {
        error!("error running downloader: {}", err);
    }

    telemetry::try_flush_and_close();

    result
}

fn load_config(opt: &RunOpt) -> Result<StaticConfig> {
    info!("loading downloader config");
    let config = match &opt.config_path {
        Some(config_path) => StaticConfig::from_file(config_path)?,
        None => StaticConfig::from_env()?,
    };

    Ok(config)
}

async fn run_downloader(config: StaticConfig, opt: &RunOpt) -> Result<()> {
    telemetry::set_property(EventData::MachineId(get_machine_id().await?));
    telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));

    let setup_inst = setup::Setup { config };
    setup_inst.run(&opt.onefuzz_path).await?;

    if opt.start_supervisor {
        setup_inst.launch_supervisor(&opt.config_path).await?;
    }

    Ok(())
}

fn init_telemetry(config: &StaticConfig) {
    let inst_key = config.instrumentation_key;
    let tele_key = config.telemetry_key;
    telemetry::set_appinsights_clients(inst_key, tele_key);
}
