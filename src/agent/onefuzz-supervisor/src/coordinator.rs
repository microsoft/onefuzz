// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use downcast_rs::Downcast;
use onefuzz::{auth::AccessToken, http::ResponseExt, process::Output};
use reqwest::{Client, RequestBuilder, Response, StatusCode};
use reqwest_retry::{RetryCheck, SendRetry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS};
use serde::Serialize;
use uuid::Uuid;

use crate::commands::SshKeyInfo;
use crate::config::Registration;
use crate::work::{TaskId, WorkSet};
use crate::worker::WorkerEvent;

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct StopTask {
    pub task_id: TaskId,
}

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum NodeCommand {
    AddSshKey(SshKeyInfo),
    StopTask(StopTask),
    Stop {},
    NoNewWork {},
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

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
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
#[serde(rename_all = "snake_case", tag = "state", content = "data")]
pub enum StateUpdateEvent {
    Init,
    Free,
    SettingUp {
        tasks: Vec<TaskId>,
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
    task_id: Uuid,
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
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
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct TaskInfo {
    job_id: Uuid,
    task_id: Uuid,
    state: TaskState,
}

#[async_trait]
pub trait ICoordinator: Downcast {
    async fn poll_commands(&mut self) -> Result<Option<NodeCommand>>;

    async fn emit_event(&mut self, event: NodeEvent) -> Result<()>;

    async fn can_schedule(&mut self, work: &WorkSet) -> Result<CanSchedule>;
}

impl_downcast!(ICoordinator);

#[async_trait]
impl ICoordinator for Coordinator {
    async fn poll_commands(&mut self) -> Result<Option<NodeCommand>> {
        self.poll_commands().await
    }

    async fn emit_event(&mut self, event: NodeEvent) -> Result<()> {
        self.emit_event(event).await
    }

    async fn can_schedule(&mut self, work_set: &WorkSet) -> Result<CanSchedule> {
        self.can_schedule(work_set).await
    }
}

pub struct Coordinator {
    client: Client,
    registration: Registration,
    token: AccessToken,
}

impl Coordinator {
    pub async fn new(registration: Registration) -> Result<Self> {
        let client = Client::new();
        let token = registration.config.credentials.access_token().await?;

        Ok(Self {
            client,
            registration,
            token,
        })
    }

    /// Poll the command endpoint once for a new command, if any.
    ///
    /// If the request fails due to an expired access token, we will retry once
    /// with a fresh one.
    pub async fn poll_commands(&mut self) -> Result<Option<NodeCommand>> {
        let response = self.send(RequestType::PollCommands).await?;
        let data = response.bytes().await?;
        let pending: PendingNodeCommand = serde_json::from_slice(&data)?;

        if let Some(envelope) = pending.envelope {
            let request = RequestType::ClaimCommand(envelope.message_id);
            self.send(request).await?;

            Ok(Some(envelope.command))
        } else {
            Ok(None)
        }
    }

    pub async fn emit_event(&mut self, event: NodeEvent) -> Result<()> {
        let envelope = NodeEventEnvelope {
            event,
            machine_id: self.registration.machine_id,
        };
        let request = RequestType::EmitEvent(&envelope);
        self.send(request).await?;

        Ok(())
    }

    async fn can_schedule(&mut self, work_set: &WorkSet) -> Result<CanSchedule> {
        let request = RequestType::CanSchedule(work_set);
        let response = self.send(request).await?;

        let can_schedule: CanSchedule = response.json().await?;

        Ok(can_schedule)
    }

    // The lifetime is needed by an argument type. We can't make it anonymous,
    // as clippy suggests, because `'_` is not allowed in this binding site.
    #[allow(clippy::needless_lifetimes)]
    async fn send<'a>(&mut self, request_type: RequestType<'a>) -> Result<Response> {
        let request = self.get_request_builder(request_type.clone());
        let mut response = request
            .send_retry(
                |code| match code {
                    StatusCode::UNAUTHORIZED => RetryCheck::Fail,
                    _ => RetryCheck::Retry,
                },
                DEFAULT_RETRY_PERIOD,
                MAX_RETRY_ATTEMPTS,
            )
            .await
            .context("Coordinator.send")?;

        if response.status() == StatusCode::UNAUTHORIZED {
            debug!("access token expired, renewing");

            // If we didn't succeed due to authorization, refresh our token,
            self.token = self.registration.config.credentials.access_token().await?;

            debug!("retrying request after refreshing access token");

            // And try one more time.
            let request = self.get_request_builder(request_type);
            response = request
                .send_retry_default()
                .await
                .context("Coordinator.send after refreshing access token")?;
        };

        // We've retried if we got a `401 Unauthorized`. If it happens again, we
        // really want to bail this time.
        let response = response
            .error_for_status_with_body()
            .await
            .context("Coordinator.send status body")?;

        Ok(response)
    }

    fn get_request_builder(&self, request_type: RequestType<'_>) -> RequestBuilder {
        match request_type {
            RequestType::PollCommands => self.poll_commands_request(),
            RequestType::ClaimCommand(message_id) => self.claim_command_request(message_id),
            RequestType::EmitEvent(event) => self.emit_event_request(event),
            RequestType::CanSchedule(work_set) => self.can_schedule_request(work_set),
        }
    }

    fn poll_commands_request(&self) -> RequestBuilder {
        let request = PollCommandsRequest {
            machine_id: self.registration.machine_id,
        };

        let url = self.registration.dynamic_config.commands_url.clone();
        let request_builder = self
            .client
            .get(url)
            .bearer_auth(self.token.secret().expose_ref())
            .json(&request);

        request_builder
    }

    fn claim_command_request(&self, message_id: String) -> RequestBuilder {
        let request = ClaimNodeCommandRequest {
            machine_id: self.registration.machine_id,
            message_id,
        };

        let url = self.registration.dynamic_config.commands_url.clone();
        let request_builder = self
            .client
            .delete(url)
            .bearer_auth(self.token.secret().expose_ref())
            .json(&request);

        request_builder
    }

    fn emit_event_request(&self, event: &NodeEventEnvelope) -> RequestBuilder {
        let url = self.registration.dynamic_config.events_url.clone();
        let request_builder = self
            .client
            .post(url)
            .bearer_auth(self.token.secret().expose_ref())
            .json(event);

        request_builder
    }

    fn can_schedule_request(&self, work_set: &WorkSet) -> RequestBuilder {
        // Temporary: assume one work unit per work set.
        //
        // In the future, we will probably want the same behavior, but we will
        // need to make sure that other the work units in the set have their states
        // updated if necessary.
        let task_id = work_set.work_units[0].task_id;
        let can_schedule = CanScheduleRequest {
            machine_id: self.registration.machine_id,
            task_id,
        };

        debug!("checking if able to schedule task ID = {}", task_id);

        let mut url = self.registration.config.onefuzz_url.clone();
        url.set_path("/api/agents/can_schedule");
        let request_builder = self
            .client
            .post(url)
            .bearer_auth(self.token.secret().expose_ref())
            .json(&can_schedule);

        request_builder
    }
}

// Enum to thunk creation of requests.
//
// The upstream `Request` type is not `Clone`, so we can't retry a request
// without rebuilding it. We use this enum to dispatch to a private method,
// avoiding borrowck conflicts that occur when capturing `self`.
#[derive(Clone, Debug, Eq, PartialEq)]
enum RequestType<'a> {
    PollCommands,
    ClaimCommand(String),
    EmitEvent(&'a NodeEventEnvelope),
    CanSchedule(&'a WorkSet),
}

#[cfg(test)]
pub mod double;
