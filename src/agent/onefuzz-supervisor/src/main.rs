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

use crate::heartbeat::*;
use std::path::PathBuf;

use anyhow::Result;
use onefuzz::{
    machine_id::{get_machine_id, get_scaleset_name},
    telemetry::{self, EventData},
};
use structopt::StructOpt;

pub mod agent;
pub mod auth;
pub mod config;
pub mod coordinator;
pub mod debug;
pub mod done;
pub mod heartbeat;
pub mod reboot;
pub mod scheduler;
pub mod setup;
pub mod work;
pub mod worker;

use config::StaticConfig;

#[derive(StructOpt, Debug)]
enum Opt {
    Run(RunOpt),
    Debug(debug::DebugOpt),
    Licenses,
    Version,
}

#[derive(StructOpt, Debug)]
struct RunOpt {
    #[structopt(short, long = "--config", parse(from_os_str))]
    config_path: Option<PathBuf>,
}

fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();

    match opt {
        Opt::Run(opt) => run(opt)?,
        Opt::Debug(opt) => debug::debug(opt)?,
        Opt::Licenses => licenses()?,
        Opt::Version => version()?,
    };

    Ok(())
}

fn version() -> Result<()> {
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
    if done::is_agent_done()? {
        verbose!(
            "agent is done, remove lock ({}) to continue",
            done::done_path()?.display()
        );
        return Ok(());
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

    let config = match &opt.config_path {
        Some(config_path) => StaticConfig::from_file(config_path)?,
        None => StaticConfig::from_env()?,
    };

    init_telemetry(&config);

    Ok(config)
}

async fn run_agent(config: StaticConfig) -> Result<()> {
    telemetry::set_property(EventData::InstanceId(config.instance_id));
    telemetry::set_property(EventData::MachineId(get_machine_id().await?));
    telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));
    let scaleset = get_scaleset_name().await?;
    if let Some(scaleset_name) = &scaleset {
        telemetry::set_property(EventData::ScalesetId(scaleset_name.to_string()));
    }

    let registration = match config::Registration::load_existing(config.clone()).await {
        Ok(registration) => registration,
        Err(_) => {
            if scaleset.is_some() {
                config::Registration::create_managed(config.clone()).await?
            } else {
                config::Registration::create_unmanaged(config.clone()).await?
            }
        }
    };
    verbose!("current registration: {:?}", registration);

    let coordinator = coordinator::Coordinator::new(registration.clone()).await?;
    verbose!("initialized coordinator");

    let mut reboot = reboot::Reboot;
    let scheduler = reboot.load_context().await?.into();
    verbose!("loaded scheduler: {}", scheduler);

    let work_queue = work::WorkQueue::new(registration.clone());

    let agent_heartbeat = match config.heartbeat_queue {
        Some(url) => Some(init_agent_heartbeat(url).await?),
        None => None,
    };
    let mut agent = agent::Agent::new(
        Box::new(coordinator),
        Box::new(reboot),
        scheduler,
        Box::new(setup::SetupRunner),
        Box::new(work_queue),
        Box::new(worker::WorkerRunner),
        agent_heartbeat,
    );

    info!("running agent");

    agent.run().await?;

    info!("supervisor agent finished");

    Ok(())
}

fn init_telemetry(config: &StaticConfig) {
    let inst_key = config.instrumentation_key;
    let tele_key = config.telemetry_key;
    telemetry::set_appinsights_clients(inst_key, tele_key);
}
