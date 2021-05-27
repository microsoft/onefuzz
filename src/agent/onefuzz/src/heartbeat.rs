// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{jitter::random_delay, utils::CheckNotify};
use anyhow::Result;
use futures::Future;
use reqwest::Url;
use std::{
    collections::HashSet,
    sync::{Arc, Mutex},
    time::Duration,
};
use storage_queue::QueueClient;
use tokio::{sync::Notify, task, task::JoinHandle};

const DEFAULT_HEARTBEAT_PERIOD: Duration = Duration::from_secs(60 * 5);

pub struct HeartbeatContext<TContext, T> {
    pub state: TContext,
    pub queue_client: QueueClient,
    pub pending_messages: Mutex<HashSet<T>>,
    pub cancelled: Notify,
}

pub struct HeartbeatClient<TContext, T>
where
    T: Clone + Send + Sync,
{
    pub context: Arc<HeartbeatContext<TContext, T>>,
    pub heartbeat_process: JoinHandle<Result<()>>,
}

impl<TContext, T> Drop for HeartbeatClient<TContext, T>
where
    T: Clone + Sync + Send,
{
    fn drop(&mut self) {
        self.context.cancelled.notify_one();
    }
}

impl<TContext, T> HeartbeatClient<TContext, T>
where
    T: Clone + Sync + Send,
{
    pub fn drain_current_messages(context: Arc<HeartbeatContext<TContext, T>>) -> Vec<T> {
        let lock = context.pending_messages.lock();
        let mut messages = lock.unwrap();
        let drain = messages.iter().cloned().collect::<Vec<T>>();
        messages.clear();
        drain
    }

    pub async fn start_background_process<Fut>(
        queue_url: Url,
        messages: Arc<Mutex<HashSet<T>>>,
        cancelled: Arc<Notify>,
        heartbeat_period: Duration,
        flush: impl Fn(Arc<QueueClient>, Arc<Mutex<HashSet<T>>>) -> Fut,
    ) -> Result<()>
    where
        Fut: Future<Output = ()> + Send,
    {
        let queue_client = Arc::new(QueueClient::new(queue_url)?);
        flush(queue_client.clone(), messages.clone()).await;
        while !cancelled.is_notified(heartbeat_period).await {
            flush(queue_client.clone(), messages.clone()).await;
        }
        flush(queue_client.clone(), messages.clone()).await;
        Ok(())
    }

    pub fn init_heartbeat<F, Fut>(
        context: TContext,
        queue_url: Url,
        initial_delay: Option<Duration>,
        heartbeat_period: Option<Duration>,
        flush: F,
    ) -> Result<HeartbeatClient<TContext, T>>
    where
        F: Fn(Arc<HeartbeatContext<TContext, T>>) -> Fut + Sync + Send + 'static,
        Fut: Future<Output = ()> + Send,
        T: 'static,
        TContext: Send + Sync + 'static,
    {
        let heartbeat_period = heartbeat_period.unwrap_or(DEFAULT_HEARTBEAT_PERIOD);
        let initial_delay = initial_delay.unwrap_or(DEFAULT_HEARTBEAT_PERIOD);

        let context = Arc::new(HeartbeatContext {
            state: context,
            queue_client: QueueClient::new(queue_url)?,
            pending_messages: Mutex::new(HashSet::<T>::new()),
            cancelled: Notify::new(),
        });

        let flush_context = context.clone();
        let heartbeat_process = task::spawn(async move {
            random_delay(initial_delay).await;
            flush(flush_context.clone()).await;
            while !flush_context.cancelled.is_notified(heartbeat_period).await {
                flush(flush_context.clone()).await;
            }
            flush(flush_context.clone()).await;
            Ok(())
        });

        Ok(HeartbeatClient {
            context,
            heartbeat_process,
        })
    }
}
