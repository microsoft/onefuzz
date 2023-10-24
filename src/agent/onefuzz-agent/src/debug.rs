// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use clap::Parser;
use onefuzz::blob::BlobContainerUrl;
use onefuzz::machine_id::MachineIdentity;
use onefuzz::process::ExitStatus;
use url::Url;
use uuid::Uuid;

use crate::coordinator::*;
use crate::work::*;
use crate::worker::*;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum DebugOpt {
    #[clap(subcommand)]
    NodeEvent(NodeEventOpt),

    RunWorker(RunWorkerOpt),
}

pub fn debug(opt: DebugOpt) -> Result<()> {
    match opt {
        DebugOpt::NodeEvent(opt) => debug_node_event(opt)?,
        DebugOpt::RunWorker(opt) => debug_run_worker(opt)?,
    }

    Ok(())
}

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum NodeEventOpt {
    StateUpdate {
        #[clap(value_enum)]
        state: NodeState,
    },

    #[clap(subcommand)]
    WorkerEvent(WorkerEventOpt),
}

fn debug_node_event(opt: NodeEventOpt) -> Result<()> {
    match opt {
        NodeEventOpt::StateUpdate { state } => debug_node_event_state_update(state)?,
        NodeEventOpt::WorkerEvent(opt) => debug_node_event_worker_event(opt)?,
    }

    Ok(())
}

fn debug_node_event_state_update(state: NodeState) -> Result<()> {
    let event = match state {
        NodeState::Init => StateUpdateEvent::Init,
        NodeState::Free => StateUpdateEvent::Free,
        NodeState::SettingUp => {
            let tasks = vec![
                SettingUpData {
                    task_id: Uuid::new_v4(),
                    job_id: Uuid::new_v4(),
                },
                SettingUpData {
                    task_id: Uuid::new_v4(),
                    job_id: Uuid::new_v4(),
                },
            ];
            StateUpdateEvent::SettingUp { task_data: tasks }
        }
        NodeState::Rebooting => StateUpdateEvent::Rebooting,
        NodeState::Ready => StateUpdateEvent::Ready,
        NodeState::Busy => StateUpdateEvent::Busy,
        NodeState::Done => StateUpdateEvent::Done {
            error: None,
            script_output: None,
        },
    };
    let event = event.into();
    print_json(into_envelope(event))
}

#[derive(Parser, Debug)]
pub enum WorkerEventOpt {
    Running,
    Done {
        #[clap(short, long)]
        code: Option<i32>,

        #[clap(short, long)]
        signal: Option<i32>,
    },
}

fn debug_node_event_worker_event(opt: WorkerEventOpt) -> Result<()> {
    let task_id = uuid::Uuid::new_v4();
    let job_id = uuid::Uuid::new_v4();

    let event = match opt {
        WorkerEventOpt::Running => WorkerEvent::Running { job_id, task_id },
        WorkerEventOpt::Done { code, signal } => {
            let (code, signal) = match (code, signal) {
                // Default to ok exit.
                (None, None) => (Some(0), None),
                // Prioritize signal.
                (Some(_), Some(s)) => (None, Some(s)),
                _ => (code, signal),
            };
            let success = code == Some(0);
            let exit_status = ExitStatus {
                code,
                signal,
                success,
            };
            let stderr = "stderr output goes here".into();
            let stdout = "stdout output goes here".into();
            WorkerEvent::Done {
                exit_status,
                stderr,
                stdout,
                job_id,
                task_id,
            }
        }
    };
    let event = NodeEvent::WorkerEvent(event);

    print_json(into_envelope(event))
}

fn into_envelope(event: NodeEvent) -> NodeEventEnvelope {
    let machine_id = uuid::Uuid::new_v4();
    NodeEventEnvelope { event, machine_id }
}

fn print_json(data: impl serde::Serialize) -> Result<()> {
    let json = serde_json::to_string_pretty(&data)?;
    println!("{json}");

    Ok(())
}

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub struct RunWorkerOpt {
    #[clap(long)]
    config: PathBuf,

    #[clap(long)]
    setup_url: Url,

    #[clap(long)]
    script: bool,

    #[clap(long)]
    extra_url: Option<Url>,
}

fn debug_run_worker(opt: RunWorkerOpt) -> Result<()> {
    let config = std::fs::read_to_string(opt.config)?;

    let task_id: Uuid = {
        use serde_json::*;

        let config: Value = serde_json::from_str(&config)?;
        let task_id = config["task_id"].to_string();
        serde_json::from_str(&task_id)?
    };

    let work_unit = WorkUnit {
        config: config.into(),
        job_id: Uuid::new_v4(),
        task_id,
        env: std::collections::HashMap::new(),
    };
    let work_set = WorkSet {
        reboot: false,
        setup_url: BlobContainerUrl::new(opt.setup_url)?,
        extra_setup_url: opt.extra_url.map(BlobContainerUrl::new).transpose()?,
        script: opt.script,
        work_units: vec![work_unit],
    };

    let rt = tokio::runtime::Runtime::new()?;
    let events = rt.block_on(run_worker(work_set))?;

    for event in events {
        println!("{event:?}");
    }

    Ok(())
}

async fn run_worker(mut work_set: WorkSet) -> Result<Vec<WorkerEvent>> {
    use crate::setup::SetupRunner;
    let setup_runner = SetupRunner {
        machine_id: Uuid::new_v4(),
    };
    setup_runner.run(&work_set).await?;

    let mut events = vec![];
    let work_unit = work_set.work_units.pop().unwrap();
    let setup_dir = work_set.setup_dir()?;
    let extra_setup_dir = work_set.extra_setup_dir()?;
    let work_dir = work_unit.working_dir(setup_runner.machine_id)?;

    let mut worker = Worker::new(work_dir, setup_dir, extra_setup_dir, work_unit);
    while !worker.is_done() {
        worker = worker
            .update(
                &mut events,
                &mut WorkerRunner::new(MachineIdentity {
                    machine_id: Uuid::new_v4(),
                    machine_name: "debug".into(),
                    scaleset_name: None,
                }),
            )
            .await?;
    }

    Ok(events)
}
