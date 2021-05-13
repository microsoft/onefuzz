// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::onefuzz::heartbeat::HeartbeatClient;
use crate::onefuzz::machine_id::{get_machine_id, get_machine_name};
use anyhow::Result;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize, Hash, Eq, PartialEq, Clone)]
#[serde(tag = "type")]
pub enum HeartbeatData {
    TaskAlive,
    MachineAlive,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct Heartbeat {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
    data: Vec<HeartbeatData>,
}

#[derive(Clone)]
pub struct TaskContext {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
}

pub type TaskHeartbeatClient = HeartbeatClient<TaskContext, HeartbeatData>;

pub async fn init_task_heartbeat(
    queue_url: Url,
    task_id: Uuid,
    job_id: Uuid,
) -> Result<TaskHeartbeatClient> {
    let machine_id = get_machine_id().await?;
    let machine_name = get_machine_name().await?;
    let hb = HeartbeatClient::init_heartbeat(
        TaskContext {
            task_id,
            job_id,
            machine_id,
            machine_name,
        },
        queue_url,
        None,
        |context| async move {
            let task_id = context.state.task_id;
            let machine_id = context.state.machine_id;
            let machine_name = context.state.machine_name.clone();
            let job_id = context.state.job_id;

            let data = HeartbeatClient::<TaskContext, _>::drain_current_messages(context.clone());
            let _ = context
                .queue_client
                .enqueue(Heartbeat {
                    task_id,
                    job_id,
                    machine_id,
                    machine_name,
                    data,
                })
                .await;
        },
    )?;
    Ok(hb)
}

pub trait HeartbeatSender {
    fn send(&self, data: HeartbeatData) -> Result<()>;

    fn alive(&self) {
        if let Err(error) = self.send(HeartbeatData::TaskAlive) {
            error!("failed to send heartbeat: {}", error);
        }
    }
}

impl HeartbeatSender for TaskHeartbeatClient {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        let mut messages_lock = self
            .context
            .pending_messages
            .lock()
            .map_err(|_| anyhow::format_err!("Unable to acquire the lock"))?;
        messages_lock.insert(data);
        Ok(())
    }
}

impl HeartbeatSender for Option<TaskHeartbeatClient> {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        match self {
            Some(client) => client.send(data),
            None => Ok(()),
        }
    }
}
