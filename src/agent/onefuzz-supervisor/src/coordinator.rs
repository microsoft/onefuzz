// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use downcast_rs::Downcast;
use reqwest::{Client, Request, Response, StatusCode};
use serde::Serialize;
use uuid::Uuid;

use crate::auth::AccessToken;
use crate::config::Registration;
use crate::work::{TaskId, WorkSet};
use crate::worker::WorkerEvent;

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct StopTask {
    pub task_id: TaskId,
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(tag = "command_type")]
pub enum NodeCommand {
    #[serde(alias = "stop")]
    StopTask(StopTask),
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct NodeCommandEnvelope {
    pub message_id: String,
    pub command: NodeCommand,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct PendingNodeCommand {
    envelope: Option<NodeCommandEnvelope>,
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
#[serde(rename_all = "snake_case", untagged)]
pub enum NodeEvent {
    StateUpdate { state: NodeState },
    WorkerEvent { event: WorkerEvent },
}

impl From<NodeState> for NodeEvent {
    fn from(state: NodeState) -> Self {
        NodeEvent::StateUpdate { state }
    }
}

impl From<WorkerEvent> for NodeEvent {
    fn from(event: WorkerEvent) -> Self {
        NodeEvent::WorkerEvent { event }
    }
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum TaskState {
    Init,
    Waiting,
    Scheduled,
    Running,
    Stopping,
    Stopped,
    WaitJob,
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct TaskSearch {
    task_id: Uuid,
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

    async fn can_schedule(&mut self, work: &WorkSet) -> Result<bool>;
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

    async fn can_schedule(&mut self, work_set: &WorkSet) -> Result<bool> {
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
        let response = self.send_with_auth_retry(RequestType::PollCommands).await?;
        let data = response.bytes().await?;
        let pending: PendingNodeCommand = serde_json::from_slice(&data)?;

        if let Some(envelope) = pending.envelope {
            // TODO: DELETE dequeued command via `message_id`.
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
        self.send_with_auth_retry(request).await?;

        Ok(())
    }

    async fn can_schedule(&mut self, work_set: &WorkSet) -> Result<bool> {
        let request = RequestType::CanSchedule(work_set);
        let response = self.send_with_auth_retry(request).await?;

        let task_info: TaskInfo = response.json().await?;

        verbose!("task_info = {:?}", task_info);

        let can_schedule = task_info.state == TaskState::Scheduled;

        Ok(can_schedule)
    }

    // The lifetime is needed by an argument type. We can't make it anonymous,
    // as clippy suggests, because `'_` is not allowed in this binding site.
    #[allow(clippy::needless_lifetimes)]
    async fn send_with_auth_retry<'a>(
        &mut self,
        request_type: RequestType<'a>,
    ) -> Result<Response> {
        let request = self.build_request(request_type)?;
        let mut response = self.client.execute(request).await?;

        if response.status() == StatusCode::UNAUTHORIZED {
            verbose!("access token expired, renewing");

            // If we didn't succeed due to authorization, refresh our token,
            self.token = self.registration.config.credentials.access_token().await?;

            verbose!("retrying request after refreshing access token");

            // And try one more time.
            let request = self.build_request(request_type)?;
            response = self.client.execute(request).await?;
        };

        // We've retried if we got a `401 Unauthorized`. If it happens again, we
        // really want to bail this time.
        let response = response.error_for_status()?;

        Ok(response)
    }

    fn build_request<'a>(&self, request_type: RequestType<'a>) -> Result<Request> {
        match request_type {
            RequestType::PollCommands => self.poll_commands_request(),
            RequestType::EmitEvent(event) => self.emit_event_request(event),
            RequestType::CanSchedule(work_set) => self.can_schedule_request(work_set),
        }
    }

    fn poll_commands_request(&self) -> Result<Request> {
        let url = self.registration.dynamic_config.commands_url.clone();
        let request = self
            .client
            .get(url)
            .bearer_auth(self.token.secret().expose())
            .build()?;

        Ok(request)
    }

    fn emit_event_request(&self, event: &NodeEventEnvelope) -> Result<Request> {
        let url = self.registration.dynamic_config.events_url.clone();
        let request = self
            .client
            .post(url)
            .bearer_auth(self.token.secret().expose())
            .json(event)
            .build()?;

        Ok(request)
    }

    fn can_schedule_request(&self, work_set: &WorkSet) -> Result<Request> {
        // Temporary: assume one work unit per work set.
        //
        // In the future, we will probably want the same behavior, but we will
        // need to make sure that other the work units in the set have their states
        // updated if necessary.
        let task_id = work_set.work_units[0].task_id;
        let task_search = TaskSearch { task_id };

        verbose!("getting task info for task ID = {}", task_id);

        let mut url = self.registration.config.onefuzz_url.clone();
        url.set_path("/api/tasks");
        let request = self
            .client
            .get(url)
            .bearer_auth(self.token.secret().expose())
            .json(&task_search)
            .build()?;

        Ok(request)
    }
}

// Enum to thunk creation of requests.
//
// The upstream `Request` type is not `Clone`, so we can't retry a request
// without rebuilding it. We use this enum to dispatch to a private method,
// avoiding borrowck conflicts that occur when capturing `self`.
#[derive(Copy, Clone, Debug, Eq, PartialEq)]
enum RequestType<'a> {
    PollCommands,
    EmitEvent(&'a NodeEventEnvelope),
    CanSchedule(&'a WorkSet),
}

#[cfg(test)]
pub mod double;
