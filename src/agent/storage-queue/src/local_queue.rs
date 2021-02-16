// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use serde::Serialize;
use std::path::PathBuf;
use std::time::Duration;
use tokio::sync::Mutex;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);

pub struct LocalQueueMessage {
    pub data: Vec<u8>,
}

pub struct LocalQueueClient {
    pub path: PathBuf,
    sender: Mutex<yaque::Sender>,
    receiver: Mutex<yaque::Receiver>,
}

impl LocalQueueClient {
    pub fn new(queue_url: PathBuf) -> Result<Self> {
        let (sender, receiver) = yaque::channel(queue_url.clone())?;
        Ok(LocalQueueClient {
            sender: Mutex::new(sender),
            receiver: Mutex::new(receiver),
            path: queue_url,
        })
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        let body = serde_xml_rs::to_string(&data).unwrap();
        match self.sender.try_lock() {
            Ok(ref mut sender) => {
                sender.send(body.as_bytes())?;
                Ok(())
            }
            Err(_) => bail!("cant enqueue"),
        }
    }

    pub async fn pop(&self) -> Result<Option<LocalQueueMessage>> {
        match self.receiver.try_lock() {
            Ok(ref mut receiver) => {
                let data = receiver
                    .recv_timeout(tokio::time::delay_for(Duration::from_secs(1)))
                    .await?;

                Ok(data.map(|data| LocalQueueMessage {
                    data: data.into_inner(),
                }))
            }
            Err(_) => bail!("cant enqueue"),
        }
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
