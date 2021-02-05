// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);
pub mod azure_queue;
pub mod local_queue;

use azure_queue::{AzureQueueClient, AzureQueueMessage};

pub enum QueueClient {
    AzureQueue(AzureQueueClient),
}

impl QueueClient {
    pub fn new(queue_url: Url) -> Self {
        QueueClient::AzureQueue(AzureQueueClient::new(queue_url))
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        match self {
            QueueClient::AzureQueue(queue_client) => queue_client.enqueue(data).await,
        }
    }

    pub async fn pop(&mut self) -> Result<Option<Message>> {
        match self {
            QueueClient::AzureQueue(queue_client) => {
                let message = queue_client.pop().await?;
                Ok(message.map(Message::QueueMessage))
            }
        }
    }
}

// #[derive(Clone)]
pub enum Message {
    QueueMessage(AzureQueueMessage),
    // LocalQueueMessage(LocalQueueMessage<'a, T>)
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Receipt {
    // Unique ID of the associated queue message.
    pub message_id: Uuid,

    // Opaque data that licenses message deletion.
    pub pop_receipt: String,
}

impl Message {
    pub fn get<T: DeserializeOwned>(&self) -> Result<T> {
        match self {
            Message::QueueMessage(message) => {
                let data = message.get()?;
                Ok(data)
            }
            // Message::LocalQueueMessage(message) => {
            //     Ok(serde_json::from_slice(&*message.data)?)
            // }
        }
    }

    pub async fn claim<T: DeserializeOwned>(self) -> Result<T> {
        match self {
            Message::QueueMessage(message) => Ok(message.claim().await?), // Message::LocalQueueMessage(message) => {
                                                                          //     let value = message.data.into_inner();
                                                                          //     Ok(serde_json::from_slice(value))

                                                                          // }
        }
    }

    pub async fn delete(self) -> Result<()> {
        match self {
            Message::QueueMessage(message) => Ok(message.delete().await?), // Message::LocalQueueMessage(message) => {
                                                                           //     let value = message.data.into_inner();
                                                                           //     Ok(serde_json::from_slice(value))

                                                                           // }
        }
    }

    pub fn parse<T>(&self, parser: impl FnOnce(&[u8]) -> Result<T>) -> Result<T> {
        match self {
            Message::QueueMessage(message) => message.parse(parser), // Message::LocalQueueMessage(message) => {
                                                                     //     let value = message.data.into_inner();
                                                                     //     Ok(serde_json::from_slice(value))

                                                                     // }
        }
    }

    // pub fn id(&self) -> Uuid {
    //     match self {
    //         Message::QueueMessage(message) => message.receipt.message_id,
    //         // todo: add meaning full id if possible
    //         Message::LocalQueueMessage(_message) => Uuid::new_v4(),
    //     }
    // }
}
