// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// use crate::job_result::JobResultClient
use anyhow::Result;
use async_trait::async_trait;
// use futures::Future;
use reqwest::Url;
use serde::{self, Deserialize, Serialize};
use std::{
    collections::HashSet,
    sync::{Arc, Mutex},
    // time::Duration,
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
}

#[derive(Debug, Deserialize, Serialize, Clone)]
struct JobResult {
    task_id: Uuid,
    job_id: Uuid,
    machine_id: Uuid,
    machine_name: String,
    data: Vec<JobResultData>,
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
    // pub job_result_process: JoinHandle<Result<()>>,
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
    // pub fn drain_current_messages(context: Arc<JobResultContext<TContext, T>>) -> Vec<T> {
    //     let lock = context.pending_messages.lock();
    //     let mut messages = lock.unwrap();
    //     let drain = messages.iter().cloned().collect::<Vec<T>>();
    //     messages.clear();
    //     drain
    // }

    // pub async fn start_background_process<Fut>(
    //     queue_url: Url,
    //     messages: Arc<Mutex<HashSet<T>>>,
    //     flush: impl Fn(Arc<QueueClient>, Arc<Mutex<HashSet<T>>>) -> Fut,
    // ) -> Result<()>
    // where
    //     Fut: Future<Output = ()> + Send,
    // {
    //     let queue_client = Arc::new(QueueClient::new(queue_url)?);
    //     flush(queue_client.clone(), messages.clone()).await;
    //     // while !cancelled.is_notified(job_result_period).await {
    //     //     flush(queue_client.clone(), messages.clone()).await;
    //     // }
    //     flush(queue_client.clone(), messages.clone()).await;
    //     Ok(())
    // }

    pub fn init_job_result(
        context: TContext,
        queue_url: Url,
        // initial_delay: Option<Duration>,
        // job_result_period: Option<Duration>,
        // flush: F,
    ) -> Result<JobResultClient<TContext, T>>
    where
        // F: Fn(Arc<JobResultContext<TContext, T>>) -> Fut + Sync + Send + 'static,
        // Fut: Future<Output = ()> + Send,
        T: 'static,
        TContext: Send + Sync + 'static,
    {
        // let job_result_period = job_result_period.unwrap_or(DEFAULT_RESULT_PERIOD);

        let context = Arc::new(JobResultContext {
            state: context,
            queue_client: QueueClient::new(queue_url)?,
            pending_messages: Mutex::new(HashSet::<T>::new()),
            cancelled: Notify::new(),
        });

        // let flush_context = context.clone();
        // let job_result_process = task::spawn(async move {
        // if let Some(initial_delay) = initial_delay {
        //     sleep(initial_delay).await;
        // }
        // flush(flush_context.clone()).await;
        // while !flush_context.cancelled.is_notified(job_result_period).await {
        //     flush(flush_context.clone()).await;
        // }
        // flush(flush_context.clone()).await;
        //     Ok(())
        // });

        Ok(JobResultClient {
            context,
            // None,
        })
    }
}

pub type TaskJobResultClient = JobResultClient<TaskContext, JobResultData>;

pub async fn init_job_result(
    queue_url: Url,
    task_id: Uuid,
    job_id: Uuid,
    // initial_delay: Option<Duration>,
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
        // initial_delay,
        // None,
        // |context| async move {
        //     let task_id = context.state.task_id;
        //     let machine_id = context.state.machine_id;
        //     let machine_name = context.state.machine_name.clone();
        //     let job_id = context.state.job_id;

        //     let data = JobResultClient::<TaskContext, _>::drain_current_messages(context.clone());
        //     let _ = context
        //         .queue_client
        //         .enqueue(JobResult {
        //             task_id,
        //             job_id,
        //             machine_id,
        //             machine_name,
        //             data,
        //         })
        //         .await;
        // },
    )?;
    Ok(hb)
}

#[async_trait]
pub trait JobResultSender {
    async fn send_direct(&self, data: JobResultData) -> Result<()>;
}

#[async_trait]
impl JobResultSender for TaskJobResultClient {
    async fn send_direct(&self, data: JobResultData) -> Result<()> {
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
            })
            .await;
        Ok(())
    }
}

#[async_trait]
impl JobResultSender for Option<TaskJobResultClient> {
    async fn send_direct(&self, data: JobResultData) -> Result<()> {
        match self {
            Some(client) => client.send_direct(data).await,
            None => Ok(()),
        }
    }
}
