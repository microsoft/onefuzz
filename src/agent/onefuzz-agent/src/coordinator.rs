// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::Arc;

use anyhow::{Context, Error, Result};
use downcast_rs::Downcast;
use onefuzz::{auth::AccessToken, http::ResponseExt, process::Output};
use reqwest::{Client, RequestBuilder, Response, StatusCode};
use reqwest_retry::{
    is_auth_failure, RetryCheck, SendRetry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS,
};
use serde::Serialize;
use tokio::sync::RwLock;
use uuid::Uuid;

use crate::commands::SshKeyInfo;
use crate::config::Registration;
use crate::work::{TaskId, WorkSet};
use crate::worker::WorkerEvent;

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize, Clone)]
pub struct StopTask {
    pub task_id: TaskId,
}

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize, Clone)]
#[serde(rename_all = "snake_case")]
pub enum NodeCommand {
    AddSshKey(SshKeyInfo),
    StopTask(StopTask),
    Stop {},
    StopIfFree {},
}

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct NodeCommandEnvelope {
    pub message_id: String,
    pub command: NodeCommand,
}

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct PendingNodeCommand {
    envelope: Option<NodeCommandEnvelope>,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct PollCommandsRequest {
    machine_id: Uuid,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ClaimNodeCommandRequest {
    machine_id: Uuid,
    message_id: String,
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize, clap::ValueEnum)]
#[serde(rename_all = "snake_case")]
pub enum NodeState {
    Init,
    Free,
    SettingUp,
    Rebooting,
    Ready,
    Busy,
    Done,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct NodeEventEnvelope {
    pub event: NodeEvent,
    pub machine_id: Uuid,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum NodeEvent {
    StateUpdate(StateUpdateEvent),
    WorkerEvent(WorkerEvent),
}

impl From<WorkerEvent> for NodeEvent {
    fn from(event: WorkerEvent) -> Self {
        NodeEvent::WorkerEvent(event)
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub struct SettingUpData {
    pub task_id: Uuid,
    pub job_id: Uuid,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case", tag = "state", content = "data")]
pub enum StateUpdateEvent {
    Init,
    Free,
    SettingUp {
        tasks: Vec<SettingUpData>,
    },
    Rebooting,
    Ready,
    Busy,
    Done {
        error: Option<String>,
        script_output: Option<Output>,
    },
}

impl From<StateUpdateEvent> for NodeEvent {
    fn from(event: StateUpdateEvent) -> Self {
        NodeEvent::StateUpdate(event)
    }
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum TaskState {
    Init,
    Waiting,
    Scheduled,
    SettingUp,
    Running,
    Stopping,
    Stopped,
    WaitJob,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct CanScheduleRequest {
    machine_id: Uuid,
    job_id: Uuid,
    task_id: Uuid,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct CanSchedule {
    /// If true, then the receiving node can schedule the work.
    /// Otherwise, the receiver should inspect `work_stopped`.
    pub allowed: bool,

    /// If `true`, then the work was stopped after being scheduled to the pool's
    /// work queue, but before being claimed by a node.
    ///
    /// No node in the pool may schedule the work, so the receiving node should
    /// claim (delete) and drop the work set.
    pub work_stopped: bool,

    /// contains the reason why the work was stopped or not allowed.
    pub reason: Option<String>,
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct TaskInfo {
    job_id: Uuid,
    task_id: Uuid,
    state: TaskState,
}

#[async_trait]
pub trait ICoordinator: Downcast {
    async fn poll_commands(&mut self) -> Result<Option<NodeCommand>, PollCommandError>;

    async fn emit_event(&self, event: NodeEvent) -> Result<()>;

    async fn can_schedule(&self, work: &WorkSet) -> Result<CanSchedule>;
}

impl_downcast!(ICoordinator);

#[async_trait]
impl ICoordinator for Coordinator {
    async fn poll_commands(&mut self) -> Result<Option<NodeCommand>, PollCommandError> {
        Coordinator::poll_commands(self).await
    }

    async fn emit_event(&self, event: NodeEvent) -> Result<()> {
        Coordinator::emit_event(self, event).await
    }

    async fn can_schedule(&self, work_set: &WorkSet) -> Result<CanSchedule> {
        Coordinator::can_schedule(self, work_set).await
    }
}

pub enum PollCommandError {
    RequestFailed(Error),
    RequestParseFailed(Error),
    ClaimFailed(Error),
}

pub struct Coordinator {
    client: Client,
    registration: Registration,
    token: Arc<RwLock<AccessToken>>,
}

impl Coordinator {
    pub async fn new(registration: Registration) -> Result<Self> {
        let client = Client::new();
        let token = registration.config.credentials.access_token().await?;

        Ok(Self {
            client,
            registration,
            token: Arc::new(RwLock::new(token)),
        })
    }

    /// Poll the command endpoint once for a new command, if any.
    ///
    /// If the request fails due to an expired access token, we will retry once
    /// with a fresh one.
    pub async fn poll_commands(&self) -> Result<Option<NodeCommand>, PollCommandError> {
        let request = PollCommandsRequest {
            machine_id: self.registration.machine_id,
        };

        let url = self.registration.dynamic_config.commands_url.clone();
        let request = self.client.get(url).json(&request);

        let pending: PendingNodeCommand = self
            .send_request(request)
            .await
            .context("PollCommands")
            .map_err(PollCommandError::RequestFailed)?
            .json()
            .await
            .context("parsing PollCommands response")
            .map_err(PollCommandError::RequestParseFailed)?;

        if let Some(envelope) = pending.envelope {
            let request = ClaimNodeCommandRequest {
                machine_id: self.registration.machine_id,
                message_id: envelope.message_id,
            };

            let url = self.registration.dynamic_config.commands_url.clone();
            let request = self.client.delete(url).json(&request);

            self.send_request(request)
                .await
                .context("ClaimCommand")
                .map_err(PollCommandError::ClaimFailed)?;

            Ok(Some(envelope.command))
        } else {
            Ok(None)
        }
    }

    pub async fn emit_event(&self, event: NodeEvent) -> Result<()> {
        let envelope = NodeEventEnvelope {
            event,
            machine_id: self.registration.machine_id,
        };

        let url = self.registration.dynamic_config.events_url.clone();
        let request = self.client.post(url).json(&envelope);

        self.send_request(request).await.context("EmitEvent")?;

        Ok(())
    }

    async fn can_schedule(&self, work_set: &WorkSet) -> Result<CanSchedule> {
        // Temporary: assume one work unit per work set.
        //
        // In the future, we will probably want the same behavior, but we will
        // need to make sure that other the work units in the set have their states
        // updated if necessary.
        let task_id = work_set.work_units[0].task_id;
        let job_id = work_set.work_units[0].job_id;
        let envelope = CanScheduleRequest {
            machine_id: self.registration.machine_id,
            job_id,
            task_id,
        };

        debug!("checking if able to schedule task ID = {}", task_id);

        let mut url = self.registration.config.onefuzz_url.clone();
        url.set_path("/api/agents/can_schedule");
        let request = self.client.post(url).json(&envelope);

        let can_schedule: CanSchedule = self
            .send_request(request)
            .await
            .context("CanSchedule")?
            .json()
            .await
            .context("parsing CanSchedule response")?;
        Ok(can_schedule)
    }

    async fn get_token(&self) -> Result<AccessToken> {
        let token = self.token.read().await;
        Ok(token.clone())
    }

    async fn refresh_token(&self) -> Result<AccessToken> {
        let mut token = self.token.write().await;
        *token = self.registration.config.credentials.access_token().await?;
        Ok(token.clone())
    }

    async fn send_request(&self, request: RequestBuilder) -> Result<Response> {
        let token = self.get_token().await?;
        let mut response = request
            .try_clone()
            .ok_or_else(|| anyhow!("unable to clone request"))?
            .bearer_auth(token.secret().expose_ref())
            .send_retry(
                |code| match code {
                    StatusCode::UNAUTHORIZED => RetryCheck::Fail,
                    _ => RetryCheck::Retry,
                },
                DEFAULT_RETRY_PERIOD,
                MAX_RETRY_ATTEMPTS,
            )
            .await
            .context("Coordinator.send");

        if is_auth_failure(&response) {
            debug!("access token expired, renewing");

            // If we didn't succeed due to authorization, refresh our token,
            let token = self.refresh_token().await?;

            debug!("retrying request after refreshing access token");

            // And try one more time.
            response = request
                .bearer_auth(token.secret().expose_ref())
                .send_retry_default()
                .await
                .context("Coordinator.send after refreshing access token");
        };

        let response = response.context("non-status error after ensuring valid access token")?;

        // We've retried if we got a `401 Unauthorized`. If it happens again, we
        // really want to bail this time.
        let response = response
            .error_for_status_with_body()
            .await
            .context("Coordinator.send status body")?;

        Ok(response)
    }

    pub fn get_machine_id(&self) -> Uuid {
        self.registration.machine_id
    }
}

#[cfg(test)]
pub mod double;
