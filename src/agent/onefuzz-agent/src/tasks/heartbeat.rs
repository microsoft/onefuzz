// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::onefuzz::machine_id::{get_machine_id, get_machine_name};
use crate::tasks::utils::CheckNotify;
use anyhow::Result;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use std::{
    collections::HashSet,
    sync::{Arc, Mutex},
    time::Duration,
};
use storage_queue::QueueClient;
use tokio::{
    sync::Notify,
    task::{self, JoinHandle},
};
use uuid::Uuid;

const DEFAULT_HEARTBEAT_PERIOD: Duration = Duration::from_secs(60 * 5);
#[derive(Debug, Deserialize, Serialize, Hash, Eq, PartialEq, Clone)]
#[serde(tag = "type")]
pub enum HeartbeatData {
    TaskAlive,
    MachineAlive,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct Heartbeat<'a> {
    task_id: Uuid,
    machine_id: Uuid,
    machine_name: &'a str,
    data: Vec<HeartbeatData>,
}

pub struct HeartbeatClient {
    cancelled: Arc<Notify>,
    messages: Arc<Mutex<HashSet<HeartbeatData>>>,
    _heartbeat_process: JoinHandle<Result<()>>,
}

impl Drop for HeartbeatClient {
    fn drop(&mut self) {
        self.cancelled.notify();
    }
}

impl HeartbeatClient {
    pub fn init(queue_url: Url, task_id: Uuid) -> Self {
        HeartbeatClient::init_with_period(queue_url, task_id, DEFAULT_HEARTBEAT_PERIOD)
    }

    pub fn init_with_period(queue_url: Url, task_id: Uuid, heartbeat_period: Duration) -> Self {
        let messages = Arc::new(Mutex::new(HashSet::new()));
        let cancelled = Arc::new(Notify::new());
        let _heartbeat_process = HeartbeatClient::start_background_process(
            task_id,
            queue_url,
            messages.clone(),
            cancelled.clone(),
            heartbeat_period,
        );
        HeartbeatClient {
            messages,
            _heartbeat_process,
            cancelled,
        }
    }

    fn drain_current_messages(messages: Arc<Mutex<HashSet<HeartbeatData>>>) -> Vec<HeartbeatData> {
        let lock = messages.lock();
        let mut messages = lock.unwrap();
        let drain = messages.iter().cloned().collect::<Vec<HeartbeatData>>();
        messages.clear();
        drain
    }

    async fn flush(
        task_id: Uuid,
        machine_id: Uuid,
        machine_name: &str,
        queue_client: &QueueClient,
        messages: Arc<Mutex<HashSet<HeartbeatData>>>,
    ) {
        let mut data = HeartbeatClient::drain_current_messages(messages.clone());
        data.push(HeartbeatData::MachineAlive);
        let _ = queue_client
            .enqueue(Heartbeat {
                task_id,
                data,
                machine_id,
                machine_name,
            })
            .await;
    }

    pub fn start_background_process(
        task_id: Uuid,
        queue_url: Url,
        messages: Arc<Mutex<HashSet<HeartbeatData>>>,
        cancelled: Arc<Notify>,
        heartbeat_period: Duration,
    ) -> JoinHandle<Result<()>> {
        let queue_client = QueueClient::new(queue_url);
        task::spawn(async move {
            let machine_id = get_machine_id().await?;
            let machine_name = get_machine_name().await?;

            HeartbeatClient::flush(
                task_id,
                machine_id,
                &machine_name,
                &queue_client,
                messages.clone(),
            )
            .await;
            while !cancelled.is_notified(heartbeat_period).await {
                HeartbeatClient::flush(
                    task_id,
                    machine_id,
                    &machine_name,
                    &queue_client,
                    messages.clone(),
                )
                .await;
            }
            HeartbeatClient::flush(
                task_id,
                machine_id,
                &machine_name,
                &queue_client,
                messages.clone(),
            )
            .await;
            Ok(())
        })
    }
}

pub trait HeartbeatSender {
    fn send(&self, data: HeartbeatData) -> Result<()>;

    fn alive(&self) {
        self.send(HeartbeatData::TaskAlive).unwrap()
    }
}

impl HeartbeatSender for HeartbeatClient {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        let mut messages_lock = self.messages.lock().unwrap();
        messages_lock.insert(data);
        Ok(())
    }
}

impl HeartbeatSender for Option<HeartbeatClient> {
    fn send(&self, data: HeartbeatData) -> Result<()> {
        match self {
            Some(client) => client.send(data),
            None => Ok(()),
        }
    }
}
