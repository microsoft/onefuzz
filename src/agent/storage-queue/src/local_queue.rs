// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use reqwest::Url;
use serde::{Deserialize, Serialize};
use std::path::Path;
use std::{io::Read, time::Duration};
use tokio::time::delay_for;
use uuid::Uuid;

use yaque::{self, channel, queue::RecvGuard, Sender};

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);

pub struct LocalQueueMessage<'a> {
    pub data: RecvGuard<'a, Vec<u8>>,
}
pub struct LocalQueueClient {
    sender: yaque::Sender,
    receiver: yaque::Receiver,
}

impl LocalQueueClient {
    pub fn new(queue_url: impl AsRef<Path>) -> Result<Self> {
        let (sender, receiver) = yaque::channel(queue_url)?;
        Ok(LocalQueueClient { sender, receiver })
    }

    pub async fn enqueue(&mut self, data: impl Serialize) -> Result<()> {
        let body = serde_xml_rs::to_string(&data).unwrap();
        self.sender.send(body.as_bytes())?;
        Ok(())
    }

    pub async fn pop(&mut self) -> Result<Option<RecvGuard<'_, Vec<u8>>>> {
        let data = self
            .receiver
            .recv_timeout(tokio::time::delay_for(Duration::from_secs(1)))
            .await?;

        Ok(data)
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
