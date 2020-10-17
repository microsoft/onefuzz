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
    machine_id: Uuid,
    machine_name: String,
    data: Vec<HeartbeatData>,
}

#[derive(Clone)]
pub struct TaskContext {
    task_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
}

pub type TaskHeartbeatClient = HeartbeatClient<TaskContext, HeartbeatData>;

pub async fn init_task_heartbeat(queue_url: Url, task_id: Uuid) -> Result<TaskHeartbeatClient> {
    let machine_id = get_machine_id().await?;
    let machine_name = get_machine_name().await?;
    let hb = HeartbeatClient::init_heartbeat(
        TaskContext {
            task_id,
            machine_id,
            machine_name,
        },
        queue_url,
        None,
        |context| async move {
            let task_id = context.state.task_id;
            let machine_id = context.state.machine_id;
            let machine_name = context.state.machine_name.clone();

            let mut data =
                HeartbeatClient::<TaskContext, _>::drain_current_messages(context.clone());
            data.push(HeartbeatData::MachineAlive);
            let _ = context
                .queue_client
                .enqueue(Heartbeat {
                    task_id,
                    data,
                    machine_id,
                    machine_name,
                })
                .await;
        },
    );
    Ok(hb)
}

pub trait HeartbeatSender {
    fn send(&self, data: HeartbeatData) -> Result<()>;

    fn alive(&self) {
        self.send(HeartbeatData::TaskAlive).unwrap()
    }
}

impl HeartbeatSender for TaskHeartbeatClient {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        let mut messages_lock = self.context.pending_messages.lock().unwrap();
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
