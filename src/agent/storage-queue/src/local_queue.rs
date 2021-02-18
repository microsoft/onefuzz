// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use backoff::{future::retry_notify, ExponentialBackoff};
use serde::Serialize;
use std::path::PathBuf;
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

pub struct LocalQueueClient {
    pub path: PathBuf,
}

impl LocalQueueClient {
    pub fn new(queue_url: PathBuf) -> Result<Self> {
        Ok(LocalQueueClient { path: queue_url })
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        let send_data = || async {
            let body = serde_xml_rs::to_string(&data).unwrap();
            let mut sender = yaque::Sender::open(&self.path)?;
            sender.send(body.as_bytes())?;
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
            let mut receiver = yaque::Receiver::open(&self.path)?;
            let data = receiver
                .recv_timeout(tokio::time::delay_for(Duration::from_secs(1)))
                .await?;

            Ok(data.map(|data| LocalQueueMessage {
                data: data.into_inner(),
            }))
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

    // pub async fn pop(&mut self) -> Result<Option<Message>> {
    //     let result = self.receiver.recv().await?;

    //     String::from_utf8(&*result);
    //     let x = result.as_ref();
    //     let msg = Message::parse(&text);

    //     let msg = if let Some(msg) = msg {
    //         msg
    //     } else {
    //         return Ok(None);
    //     };

    //     let msg = if msg.data.is_empty() { None } else { Some(msg) };

    //     Ok(msg)

    // }

    // pub async fn delete(&mut self, receipt: impl Into<Receipt>) -> Result<()> {
    //     match self {
    //         QueueClient::AzureQueue(queue_client) => queue_client.delete(receipt).await
    //     }
    // }
}
