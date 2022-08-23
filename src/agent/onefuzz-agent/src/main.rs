// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate async_trait;
#[macro_use]
extern crate downcast_rs;
#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate onefuzz_telemetry;
extern crate onefuzz;

use crate::{
    config::StaticConfig, coordinator::StateUpdateEvent, heartbeat::init_agent_heartbeat,
    panic::set_panic_handler, work::WorkSet, worker::WorkerEvent,
};
use std::fs::OpenOptions;
use std::path::PathBuf;
use std::process::{Command, Stdio};

use anyhow::{Context, Result};
use clap::Parser;
use onefuzz::{
    machine_id::{get_machine_id, get_scaleset_name},
    process::ExitStatus,
};
use onefuzz_telemetry::{self as telemetry, EventData, Role};
use std::io::{self, Write};
use uuid::Uuid;

pub mod agent;
pub mod buffer;
pub mod commands;
pub mod config;
pub mod coordinator;
pub mod debug;
pub mod done;
pub mod failure;
pub mod heartbeat;
pub mod panic;
pub mod reboot;
pub mod scheduler;
pub mod setup;
pub mod work;
pub mod worker;

#[derive(Parser, Debug)]
enum Opt {
    Run(RunOpt),
    #[clap(subcommand)]
    Debug(debug::DebugOpt),
    Licenses,
    Version,
}

#[derive(Parser, Debug)]
struct RunOpt {
    #[clap(short, long = "--config", parse(from_os_str))]
    config_path: Option<PathBuf>,
    /// re-executes as a child process, recording stdout/stderr to files in
    /// the specified directory
    #[clap(short, long = "--redirect-output", parse(from_os_str))]
    redirect_output: Option<PathBuf>,
}

fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::parse();

    set_panic_handler();

    match opt {
        Opt::Run(opt) => run(opt)?,
        Opt::Debug(opt) => debug::debug(opt)?,
        Opt::Licenses => licenses()?,
        Opt::Version => version(),
    };

    Ok(())
}

fn version() {
    println!(
        "{} onefuzz:{} git:{}",
        clap::crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );
}

fn licenses() -> Result<()> {
    io::stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn redirect(opt: RunOpt) -> Result<()> {
    let log_path = opt
        .redirect_output
        .expect("redirect should only be called with log_path");

    if !log_path.is_dir() {
        bail!("log path must be a directory: {}", log_path.display());
    }

    let run_id = Uuid::new_v4();

    let stdout_path = log_path.join(format!("{}-stdout.txt", run_id));
    let stderr_path = log_path.join(format!("{}-stdout.txt", run_id));
    let failure_path = log_path.join(format!("{}-failure.txt", run_id));

    info!(
        "saving output to files: {} {} {}",
        stdout_path.display(),
        stderr_path.display(),
        failure_path.display()
    );

    let stdout = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&stdout_path)
        .context("unable to open log file")?;
    let stderr = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&stderr_path)
        .context("unable to open log file")?;

    let mut cmd = Command::new(std::env::current_exe()?);
    cmd.stdout(Stdio::from(stdout))
        .stderr(Stdio::from(stderr))
        .arg("run");
    if let Some(path) = opt.config_path {
        cmd.arg("--config").arg(path);
    }

    let exit_status: ExitStatus = cmd
        .spawn()
        .context("unable to start child onefuzz-agent")?
        .wait()
        .context("unable to get exit status")?
        .into();

    if !exit_status.success {
        let mut log = OpenOptions::new()
            .create(true)
            .append(true)
            .open(failure_path)
            .context("unable to open log file")?;
        log.write_fmt(format_args!(
            "onefuzz-agent child failed: {:?}",
            exit_status
        ))?;
        bail!("onefuzz-agent child failed: {:?}", exit_status);
    }

    Ok(())
}

fn run(opt: RunOpt) -> Result<()> {
    if opt.redirect_output.is_some() {
        return redirect(opt);
    }

    if done::is_agent_done()? {
        debug!(
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

    let rt = tokio::runtime::Runtime::new()?;
    let result = rt.block_on(run_agent(config));

    if let Err(err) = &result {
        error!("error running supervisor agent: {:?}", err);
        if let Err(err) = failure::save_failure(err) {
            error!("unable to save failure log: {:?}", err);
        }
    }

    rt.block_on(telemetry::try_flush_and_close());

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

async fn check_existing_worksets(coordinator: &mut coordinator::Coordinator) -> Result<()> {
    // Having existing worksets at this point means the supervisor crashed. If
    // that is the case, mark each of the work units within the workset as
    // failed, then exit as a failure.

    if let Some(work) = WorkSet::load_from_fs_context().await? {
        warn!("onefuzz-agent unexpectedly identified an existing workset on start");
        let failure = match failure::read_failure() {
            Ok(value) => format!("onefuzz-agent failed: {}", value),
            Err(failure_err) => {
                warn!("unable to read failure: {:?}", failure_err);
                let logs = failure::read_logs().unwrap_or_else(|logs_err| {
                    format!(
                        "unable to read failure message or logs: {:?} {:?}",
                        failure_err, logs_err
                    )
                });
                format!("onefuzz-agent failed: {}", logs)
            }
        };

        for unit in &work.work_units {
            let event = WorkerEvent::Done {
                task_id: unit.task_id,
                stdout: "".to_string(),
                stderr: failure.clone(),
                exit_status: ExitStatus {
                    code: Some(1),
                    signal: None,
                    success: false,
                },
            };
            coordinator.emit_event(event.into()).await?;
        }

        let event = StateUpdateEvent::Done {
            error: Some(failure),
            script_output: None,
        };
        coordinator.emit_event(event.into()).await?;

        // force set done semaphore, as to not prevent the supervisor continuing
        // to report the workset as failed.
        done::set_done_lock().await?;
        anyhow::bail!(
            "failed to start due to pre-existing workset config: {}",
            WorkSet::context_path()?.display()
        );
    }

    Ok(())
}

async fn run_agent(config: StaticConfig) -> Result<()> {
    telemetry::set_property(EventData::InstanceId(config.instance_id));
    telemetry::set_property(EventData::MachineId(get_machine_id().await?));
    telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));
    telemetry::set_property(EventData::Role(Role::Supervisor));
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
    debug!("current registration: {:?}", registration);

    let mut coordinator = coordinator::Coordinator::new(registration.clone()).await?;
    debug!("initialized coordinator");

    let mut reboot = reboot::Reboot;
    let reboot_context = reboot.load_context().await?;
    if reboot_context.is_none() {
        check_existing_worksets(&mut coordinator).await?;
    }
    let scheduler = reboot_context.into();
    debug!("loaded scheduler: {}", scheduler);

    let work_queue = work::WorkQueue::new(registration.clone())?;

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
    telemetry::set_appinsights_clients(
        config.instance_telemetry_key.clone(),
        config.microsoft_telemetry_key.clone(),
    );
}
