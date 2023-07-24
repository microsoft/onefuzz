// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::onefuzz::heartbeat::HeartbeatClient;
use anyhow::Result;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize, Hash, Eq, PartialEq, Clone)]
#[serde(tag = "type")]
pub enum HeartbeatData {
    TaskAlive,
    MachineAlive,
    NewCrashingInput,
    NoReproCrashingInput,
    NewReport,
    NewUniqueReport,
    NewRegressionReport,
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
    initial_delay: Option<Duration>,
    machine_id: Uuid,
    machine_name: String,
) -> Result<TaskHeartbeatClient> {
    let hb = HeartbeatClient::init_heartbeat(
        TaskContext {
            task_id,
            job_id,
            machine_id,
            machine_name,
        },
        queue_url,
        initial_delay,
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

    fn send_direct(&self, data: HeartbeatData);

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

    async fn send_direct(&self, data: HeartbeatData) {
        let task_id = self.context.state.task_id;
        let job_id = self.context.state.job_id;
        let machine_id = self.context.state.machine_id;
        let machine_name = self.context.state.machine_name.clone();

        self.context
            .queue_client
            .enqueue(Heartbeat {
                task_id,
                job_id,
                machine_id,
                machine_name,
                data: vec![data],
            })
            .await;
    }
}

impl HeartbeatSender for Option<TaskHeartbeatClient> {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        match self {
            Some(client) => client.send(data),
            None => Ok(()),
        }
    }

    fn send_direct(&self, data: HeartbeatData) {
        match self {
            Some(client) => client.send_direct(data),
            None => (),
        }
    }
}
