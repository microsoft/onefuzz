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
    MachineAlive,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct Heartbeat {
    node_id: Uuid,
    machine_name: String,
    data: Vec<HeartbeatData>,
}

#[derive(Clone)]
pub struct AgentContext {
    node_id: Uuid,
    machine_name: String,
}

pub type AgentHeartbeatClient = HeartbeatClient<AgentContext, HeartbeatData>;

pub async fn init_agent_heartbeat(queue_url: Url) -> Result<AgentHeartbeatClient> {
    let node_id = get_machine_id().await?;
    let machine_name = get_machine_name().await?;
    let hb = HeartbeatClient::init_heartbeat(
        AgentContext {
            node_id,
            machine_name,
        },
        queue_url,
        None,
        |context| async move {
            let data = HeartbeatClient::drain_current_messages(context.clone());
            let _ = context
                .queue_client
                .enqueue(Heartbeat {
                    node_id: context.state.node_id,
                    data,
                    machine_name: context.state.machine_name.clone(),
                })
                .await;
        },
    );
    Ok(hb)
}

pub trait HeartbeatSender {
    fn send(&self, data: HeartbeatData);

    fn alive(&self) {
        self.send(HeartbeatData::MachineAlive)
    }
}

impl HeartbeatSender for AgentHeartbeatClient {
    fn send(&self, data: HeartbeatData) {
        let mut messages_lock = self.context.pending_messages.lock().unwrap();
        messages_lock.insert(data);
    }
}

impl HeartbeatSender for Option<AgentHeartbeatClient> {
    fn send(&self, data: HeartbeatData) {
        if let Some(client) = self {
            client.send(data)
        }
    }
}
