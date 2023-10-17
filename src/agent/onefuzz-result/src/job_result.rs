// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use chrono::DateTime;
pub use chrono::Utc;
use onefuzz_telemetry::warn;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::Arc;
use storage_queue::QueueClient;
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize, Hash, Eq, PartialEq, Clone)]
#[serde(tag = "type")]
pub enum JobResultData {
    NewCrashingInput,
    NoReproCrashingInput,
    NewReport,
    CrashReported,
    NewUniqueReport,
    NewRegressionReport,
    NewCoverage,
    NewCrashDump,
    CoverageData,
    RuntimeStats,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct JobResult {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
    created_at: DateTime<Utc>,
    data: JobResultData,
    value: HashMap<String, f64>,
}

#[derive(Clone)]
pub struct TaskContext {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
}

pub struct JobResultContext<TaskContext> {
    pub state: TaskContext,
    pub queue_client: QueueClient,
}

pub struct JobResultClient<TaskContext> {
    pub context: Arc<JobResultContext<TaskContext>>,
}

impl<TaskContext> JobResultClient<TaskContext> {
    pub fn init_job_result(
        context: TaskContext,
        queue_url: Url,
    ) -> Result<JobResultClient<TaskContext>>
    where
        TaskContext: Send + Sync + 'static,
    {
        let context = Arc::new(JobResultContext {
            state: context,
            queue_client: QueueClient::new(queue_url)?,
        });

        Ok(JobResultClient { context })
    }
}

pub type TaskJobResultClient = JobResultClient<TaskContext>;

pub async fn init_job_result(
    queue_url: Url,
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
) -> Result<TaskJobResultClient> {
    let hb = JobResultClient::init_job_result(
        TaskContext {
            task_id,
            job_id,
            machine_id,
            machine_name,
        },
        queue_url,
    )?;
    Ok(hb)
}

#[async_trait]
pub trait JobResultSender {
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, f64>);
}

#[async_trait]
impl JobResultSender for TaskJobResultClient {
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, f64>) {
        let task_id = self.context.state.task_id;
        let job_id = self.context.state.job_id;
        let machine_id = self.context.state.machine_id;
        let machine_name = self.context.state.machine_name.clone();
        let created_at = chrono::Utc::now();
        let _ = self
            .context
            .queue_client
            .enqueue(JobResult {
                task_id,
                job_id,
                machine_id,
                machine_name,
                created_at,
                data,
                value,
            })
            .await;
    }
}

#[async_trait]
impl JobResultSender for Option<TaskJobResultClient> {
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, f64>) {
        match self {
            Some(client) => client.send_direct(data, value).await,
            None => warn!("Failed to send Job Result message data from agent."),
        }
    }
}
