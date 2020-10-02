// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate async_trait;
#[macro_use]
extern crate downcast_rs;
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

pub mod agent;
pub mod auth;
pub mod config;
pub mod coordinator;
pub mod debug;
pub mod process;
pub mod reboot;
pub mod scheduler;
pub mod setup;
pub mod work;
pub mod worker;
pub mod done;

use config::StaticConfig;

#[derive(StructOpt, Debug)]
enum Opt {
    Run(RunOpt),
    Debug(debug::DebugOpt),
    Licenses,
}

#[derive(StructOpt, Debug)]
struct RunOpt {
    #[structopt(short, long = "--config", parse(from_os_str))]
    config_path: PathBuf,
}

fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();

    match opt {
        Opt::Run(opt) => run(opt)?,
        Opt::Debug(opt) => debug::debug(opt)?,
        Opt::Licenses => licenses()?,
    };

    Ok(())
}

fn licenses() -> Result<()> {
    use std::io::{self, Write};
    io::stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn run(opt: RunOpt) -> Result<()> {
    info!(
        "{} onefuzz:{} git:{}",
        crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );

    if done::is_agent_done()? {
        verbose!("agent is done, remove lock to continue");
        return Ok(())
    }

    // We can't send telemetry if this fails.
    let config = load_config(opt);

    if let Err(err) = &config {
        error!("error loading supervisor agent config: {:?}", err);
    }

    let config = config?;

    let mut rt = tokio::runtime::Runtime::new()?;
    let result = rt.block_on(run_agent(config));

    if let Err(err) = &result {
        error!("error running supervisor agent: {}", err);
    }

    telemetry::try_flush_and_close();

    result
}

fn load_config(opt: RunOpt) -> Result<StaticConfig> {
    info!("loading supervisor agent config");

    let data = std::fs::read(&opt.config_path)?;
    let config = StaticConfig::new(&data)?;
    verbose!("loaded static config from: {}", opt.config_path.display());

    init_telemetry(&config);

    Ok(config)
}

async fn run_agent(config: StaticConfig) -> Result<()> {
    telemetry::set_property(EventData::MachineId(get_machine_id().await?));
    let registration = config::Registration::create_managed(config.clone()).await?;
    verbose!("created managed registration: {:?}", registration);

    let coordinator = coordinator::Coordinator::new(registration.clone()).await?;
    verbose!("initialized coordinator");

    let mut reboot = reboot::Reboot;
    let scheduler = reboot.load_context().await?.into();
    verbose!("loaded scheduler: {}", scheduler);

    let work_queue = work::WorkQueue::new(registration.clone());

    let mut agent = agent::Agent::new(
        Box::new(coordinator),
        Box::new(reboot),
        scheduler,
        Box::new(setup::SetupRunner),
        Box::new(work_queue),
        Box::new(worker::WorkerRunner),
    );

    info!("running supervisor agent");

    agent.run().await?;

    info!("supervisor agent finished");

    Ok(())
}

fn init_telemetry(config: &StaticConfig) {
    let inst_key = config
        .instrumentation_key
        .map(|k| k.to_string())
        .unwrap_or_else(String::default);
    let tele_key = config
        .telemetry_key
        .map(|k| k.to_string())
        .unwrap_or_else(String::default);

    telemetry::set_appinsights_clients(inst_key, tele_key);
}
