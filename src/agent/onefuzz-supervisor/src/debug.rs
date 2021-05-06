// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use onefuzz::blob::BlobContainerUrl;
use onefuzz::process::ExitStatus;
use structopt::StructOpt;
use url::Url;
use uuid::Uuid;

use crate::coordinator::*;
use crate::work::*;
use crate::worker::*;

#[derive(StructOpt, Debug)]
#[structopt(rename_all = "snake_case")]
pub enum DebugOpt {
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

#[derive(StructOpt, Debug)]
#[structopt(rename_all = "snake_case")]
pub enum NodeEventOpt {
    StateUpdate {
        #[structopt(parse(try_from_str = serde_json::from_str))]
        state: NodeState,
    },
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
            let tasks = vec![Uuid::new_v4(), Uuid::new_v4(), Uuid::new_v4()];
            StateUpdateEvent::SettingUp { tasks }
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

#[derive(StructOpt, Debug)]
pub enum WorkerEventOpt {
    Running,
    Done {
        #[structopt(short, long)]
        code: Option<i32>,

        #[structopt(short, long)]
        signal: Option<i32>,
    },
}

fn debug_node_event_worker_event(opt: WorkerEventOpt) -> Result<()> {
    let task_id = uuid::Uuid::new_v4();

    let event = match opt {
        WorkerEventOpt::Running => WorkerEvent::Running { task_id },
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
    println!("{}", json);

    Ok(())
}

#[derive(StructOpt, Debug)]
#[structopt(rename_all = "snake_case")]
pub struct RunWorkerOpt {
    #[structopt(long)]
    config: PathBuf,

    #[structopt(long)]
    setup_url: Url,

    #[structopt(long)]
    script: bool,
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
    };
    let work_set = WorkSet {
        reboot: false,
        setup_url: BlobContainerUrl::new(opt.setup_url)?,
        script: opt.script,
        work_units: vec![work_unit],
    };

    let rt = tokio::runtime::Runtime::new()?;
    let events = rt.block_on(run_worker(work_set))?;

    for event in events {
        println!("{:?}", event);
    }

    Ok(())
}

async fn run_worker(mut work_set: WorkSet) -> Result<Vec<WorkerEvent>> {
    use crate::setup::SetupRunner;

    SetupRunner.run(&work_set).await?;

    let mut events = vec![];
    let work_unit = work_set.work_units.pop().unwrap();
    let setup_dir = work_set.setup_dir()?;

    let mut worker = Worker::new(&setup_dir, work_unit);
    while !worker.is_done() {
        worker = worker.update(&mut events, &mut WorkerRunner).await?;
    }

    Ok(events)
}
