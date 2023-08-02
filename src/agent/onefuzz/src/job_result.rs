// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// use crate::{jitter::random_delay, utils::CheckNotify};
// use anyhow::Result;
// use futures::Future;
// use reqwest::Url;
// use std::{
//     collections::HashSet,
//     sync::{Arc, Mutex},
//     time::Duration,
// };
// use storage_queue::QueueClient;
// use tokio::{sync::Notify, task, task::JoinHandle, time::sleep};

// const DEFAULT_RESULT_PERIOD: Duration = Duration::from_secs(60 * 5);

// pub struct JobResultContext<TContext, T> {
//     pub state: TContext,
//     pub queue_client: QueueClient,
//     pub pending_messages: Mutex<HashSet<T>>,
//     pub cancelled: Notify,
// }

// pub struct JobResultClient<TContext, T>
// where
//     T: Clone + Send + Sync,
// {
//     pub context: Arc<JobResultContext<TContext, T>>,
//     pub job_result_process: JoinHandle<Result<()>>,
// }

// impl<TContext, T> Drop for JobResultClient<TContext, T>
// where
//     T: Clone + Sync + Send,
// {
//     fn drop(&mut self) {
//         self.context.cancelled.notify_one();
//     }
// }

// impl<TContext, T> JobResultClient<TContext, T>
// where
//     T: Clone + Sync + Send,
// {
//     pub fn drain_current_messages(context: Arc<JobResultContext<TContext, T>>) -> Vec<T> {
//         let lock = context.pending_messages.lock();
//         let mut messages = lock.unwrap();
//         let drain = messages.iter().cloned().collect::<Vec<T>>();
//         messages.clear();
//         drain
//     }

//     pub async fn start_background_process<Fut>(
//         queue_url: Url,
//         messages: Arc<Mutex<HashSet<T>>>,
//         cancelled: Arc<Notify>,
//         job_result_period: Duration,
//         flush: impl Fn(Arc<QueueClient>, Arc<Mutex<HashSet<T>>>) -> Fut,
//     ) -> Result<()>
//     where
//         Fut: Future<Output = ()> + Send,
//     {
//         let queue_client = Arc::new(QueueClient::new(queue_url)?);
//         flush(queue_client.clone(), messages.clone()).await;
//         while !cancelled.is_notified(job_result_period).await {
//             flush(queue_client.clone(), messages.clone()).await;
//         }
//         flush(queue_client.clone(), messages.clone()).await;
//         Ok(())
//     }

//     pub fn init_job_result<F, Fut>(
//         context: TContext,
//         queue_url: Url,
//         initial_delay: Option<Duration>,
//         job_result_period: Option<Duration>,
//         flush: F,
//     ) -> Result<JobResultClient<TContext, T>>
//     where
//         F: Fn(Arc<JobResultContext<TContext, T>>) -> Fut + Sync + Send + 'static,
//         Fut: Future<Output = ()> + Send,
//         T: 'static,
//         TContext: Send + Sync + 'static,
//     {
//         let job_result_period = job_result_period.unwrap_or(DEFAULT_RESULT_PERIOD);

//         let context = Arc::new(JobResultContext {
//             state: context,
//             queue_client: QueueClient::new(queue_url)?,
//             pending_messages: Mutex::new(HashSet::<T>::new()),
//             cancelled: Notify::new(),
//         });

//         let flush_context = context.clone();
//         let job_result_process = task::spawn(async move {
//             if let Some(initial_delay) = initial_delay {
//                 sleep(initial_delay).await;
//             } else {
//                 random_delay(job_result_period).await;
//             }
//             flush(flush_context.clone()).await;
//             while !flush_context.cancelled.is_notified(job_result_period).await {
//                 flush(flush_context.clone()).await;
//             }
//             flush(flush_context.clone()).await;
//             Ok(())
//         });

//         Ok(JobResultClient {
//             context,
//             job_result_process,
//         })
//     }
// }
