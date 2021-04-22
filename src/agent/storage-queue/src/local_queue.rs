// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{anyhow, Result};
use backoff::{future::retry_notify, ExponentialBackoff};
use queue_file::QueueFile;
use serde::Serialize;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::Duration;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);
pub const SEND_RETRY_DELAY: Duration = Duration::from_millis(500);
pub const RECEIVE_RETRY_DELAY: Duration = Duration::from_millis(500);
pub const MAX_SEND_ATTEMPTS: i32 = 5;
pub const MAX_RECEIVE_ATTEMPTS: i32 = 5;
pub const MAX_ELAPSED_TIME: Duration = Duration::from_secs(2 * 60);

pub struct LocalQueueMessage {
    pub data: Vec<u8>,
}

impl std::fmt::Debug for LocalQueueMessage {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{:?}", std::str::from_utf8(&self.data).unwrap())
    }
}

/// File backed queue
#[derive(Debug, Clone)]
pub struct FileQueueClient {
    queue: Arc<Mutex<QueueFile>>,
    pub path: PathBuf,
}

impl FileQueueClient {
    pub fn new(queue_url: PathBuf) -> Result<Self> {
        let queue = Arc::new(Mutex::new(
            queue_file::QueueFile::open(queue_url.clone())
                .map_err(|err| anyhow!("cannot open queue file {:?} : {}", queue_url, err))?,
        ));

        Ok(FileQueueClient {
            queue,
            path: queue_url,
        })
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        let send_data = || async {
            let mut buffer = Vec::new();
            serde_xml_rs::to_writer(&mut buffer, &data)
                .map_err(|_| anyhow::anyhow!("unable to deserialize"))?;
            let mut locked_q = self
                .queue
                .lock()
                .map_err(|_| anyhow::anyhow!("unable to acquire lock"))?;
            locked_q
                .add(buffer.as_slice())
                .map_err(|_| anyhow::anyhow!("unable to queue message"))?;
            Ok(())
        };

        let backoff = ExponentialBackoff {
            current_interval: SEND_RETRY_DELAY,
            initial_interval: SEND_RETRY_DELAY,
            max_elapsed_time: Some(MAX_ELAPSED_TIME),
            ..ExponentialBackoff::default()
        };
        let notify = |err, _| println!("IO error: {}", err);
        retry_notify(backoff, send_data, notify).await?;

        Ok(())
    }

    pub async fn pop(&self) -> Result<Option<LocalQueueMessage>> {
        let receive_data = || async {
            let mut locked_q = self
                .queue
                .lock()
                .map_err(|_| anyhow::anyhow!("unable to acquire lock"))?;
            let data = locked_q
                .peek()
                .map_err(|_| anyhow::anyhow!("unable to peek"))?;
            locked_q
                .remove()
                .map_err(|_| anyhow::anyhow!("unable to pop message"))?;

            let message = data.map(|d| LocalQueueMessage { data: d.to_vec() });
            Ok(message)
        };

        let backoff = ExponentialBackoff {
            current_interval: SEND_RETRY_DELAY,
            initial_interval: SEND_RETRY_DELAY,
            max_elapsed_time: Some(MAX_ELAPSED_TIME),
            ..ExponentialBackoff::default()
        };
        let notify = |err, _| println!("IO error: {}", err);
        let result = retry_notify(backoff, receive_data, notify).await?;

        Ok(result)
    }
}

use flume::{unbounded, Receiver, Sender, TryRecvError};

/// Queue based on mpsc channel
#[derive(Debug, Clone)]
pub struct ChannelQueueClient {
    sender: Arc<Mutex<Sender<Vec<u8>>>>,
    receiver: Arc<Mutex<Receiver<Vec<u8>>>>,
    pub url: reqwest::Url,
    low_resource: bool,
}

impl ChannelQueueClient {
    pub fn new() -> Result<Self> {
        let (sender, receiver) = unbounded();
        let cpus = num_cpus::get();
        let low_resource = cpus < 4;
        Ok(ChannelQueueClient {
            sender: Arc::new(Mutex::new(sender)),
            receiver: Arc::new(Mutex::new(receiver)),
            url: reqwest::Url::parse("mpsc://channel")?,
            low_resource,
        })
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        // temporary fix. Forcing a yield to allow other tasks to proceed
        // in a low core count environment such as github action machines
        if self.low_resource {
            tokio::task::yield_now().await;
        }
        let sender = self
            .sender
            .lock()
            .map_err(|_| anyhow::anyhow!("unable to acquire lock"))?;
        let mut buffer = Vec::new();
        serde_xml_rs::to_writer(&mut buffer, &data)
            .map_err(|_| anyhow::anyhow!("unable to deserialize"))?;
        sender.send(buffer)?;
        Ok(())
    }

    pub async fn pop(&self) -> Result<Option<LocalQueueMessage>> {
        // temporary fix. Forcing a yield to allow other tasks to proceed
        // in a low core count environment such as github action machines
        if self.low_resource {
            tokio::task::yield_now().await;
        }
        let receiver = self
            .receiver
            .lock()
            .map_err(|_| anyhow::anyhow!("unable to acquire lock"))?;

        match receiver.try_recv() {
            Ok(data) => Ok(Some(LocalQueueMessage { data })),
            Err(TryRecvError::Empty) => Ok(None),
            Err(err) => Err(err.into()),
        }
    }
}
