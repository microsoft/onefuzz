// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use std::collections::HashMap;
use std::{
    collections::HashSet,
    sync::{Arc, Mutex},
};
use storage_queue::QueueClient;
use tokio::sync::Notify;
use uuid::Uuid;

#[derive(Debug, Deserialize, Serialize, Hash, Eq, PartialEq, Clone)]
#[serde(tag = "type")]
pub enum JobResultData {
    NewCrashingInput,
    NoReproCrashingInput,
    NewReport,
    NewUniqueReport,
    NewRegressionReport,
    NewCoverageData,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct JobResult {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
    data: Vec<JobResultData>,
    value: HashMap<String, i64>,
}

#[derive(Clone)]
pub struct TaskContext {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
}

pub struct JobResultContext<TContext, T> {
    pub state: TContext,
    pub queue_client: QueueClient,
    pub pending_messages: Mutex<HashSet<T>>,
    pub cancelled: Notify,
}

pub struct JobResultClient<TContext, T>
where
    T: Clone + Send + Sync,
{
    pub context: Arc<JobResultContext<TContext, T>>,
}

impl<TContext, T> Drop for JobResultClient<TContext, T>
where
    T: Clone + Sync + Send,
{
    fn drop(&mut self) {
        self.context.cancelled.notify_one();
    }
}

impl<TContext, T> JobResultClient<TContext, T>
where
    T: Clone + Sync + Send,
{
    pub fn init_job_result(
        context: TContext,
        queue_url: Url,
    ) -> Result<JobResultClient<TContext, T>>
    where
        T: 'static,
        TContext: Send + Sync + 'static,
    {
        let context = Arc::new(JobResultContext {
            state: context,
            queue_client: QueueClient::new(queue_url)?,
            pending_messages: Mutex::new(HashSet::<T>::new()),
            cancelled: Notify::new(),
        });

        Ok(JobResultClient { context })
    }
}

pub type TaskJobResultClient = JobResultClient<TaskContext, JobResultData>;

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
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, i64>) -> Result<()>;
}

#[async_trait]
impl JobResultSender for TaskJobResultClient {
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, i64>) -> Result<()> {
        let task_id = self.context.state.task_id;
        let job_id = self.context.state.job_id;
        let machine_id = self.context.state.machine_id;
        let machine_name = self.context.state.machine_name.clone();

        let _ = self
            .context
            .queue_client
            .enqueue(JobResult {
                task_id,
                job_id,
                machine_id,
                machine_name,
                data: vec![data],
                value,
            })
            .await;
        Ok(())
    }
}

#[async_trait]
impl JobResultSender for Option<TaskJobResultClient> {
    async fn send_direct(&self, data: JobResultData, value: HashMap<String, i64>) -> Result<()> {
        match self {
            Some(client) => client.send_direct(data, value).await,
            None => Ok(()),
        }
    }
}
