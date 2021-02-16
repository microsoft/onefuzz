// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{anyhow, Result};
use reqwest::Url;
use serde::{de::DeserializeOwned, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);
pub mod azure_queue;
pub mod local_queue;

use azure_queue::{AzureQueueClient, AzureQueueMessage};
use local_queue::{LocalQueueClient, LocalQueueMessage};

pub enum QueueClient {
    AzureQueue(AzureQueueClient),
    LocalQueue(Box<LocalQueueClient>),
}

impl QueueClient {
    pub fn new(queue_url: Url) -> Self {
        QueueClient::AzureQueue(AzureQueueClient::new(queue_url))
    }

    pub fn get_url(self) -> Result<Url> {
        match self {
            QueueClient::AzureQueue(queue_client) => Ok(queue_client.messages_url),
            QueueClient::LocalQueue(queue_client) => {
                Url::from_file_path(queue_client.as_ref().path.clone())
                    .map_err(|_| anyhow!("invalid queue url"))
            }
        }
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        match self {
            QueueClient::AzureQueue(queue_client) => queue_client.enqueue(data).await,
            QueueClient::LocalQueue(queue_client) => queue_client.enqueue(data).await,
        }
    }

    pub async fn pop(&self) -> Result<Option<Message>> {
        match self {
            QueueClient::AzureQueue(queue_client) => {
                let message = queue_client.pop().await?;
                Ok(message.map(Message::QueueMessage))
            }
            QueueClient::LocalQueue(queue_client) => {
                let message = queue_client.pop().await?;
                Ok(message.map(Message::LocalQueueMessage))
            }
        }
    }
}

pub enum Message {
    QueueMessage(AzureQueueMessage),
    LocalQueueMessage(LocalQueueMessage),
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
            Message::LocalQueueMessage(message) => Ok(serde_json::from_slice(&*message.data)?),
        }
    }

    pub async fn claim<T: DeserializeOwned>(self) -> Result<T> {
        match self {
            Message::QueueMessage(message) => Ok(message.claim().await?),
            Message::LocalQueueMessage(message) => Ok(serde_json::from_slice(&message.data)?),
        }
    }

    pub async fn delete(&self) -> Result<()> {
        match self {
            Message::QueueMessage(message) => Ok(message.delete().await?),
            Message::LocalQueueMessage(_) => {
                // message.data.commit();
                Ok(())
            }
        }
    }

    pub fn parse<T>(&self, parser: impl FnOnce(&[u8]) -> Result<T>) -> Result<T> {
        match self {
            Message::QueueMessage(message) => message.parse(parser),
            Message::LocalQueueMessage(message) => parser(&*message.data),
        }
    }

    pub fn update_url(self, new_url: Url) -> Self {
        match self {
            Message::QueueMessage(message) => Message::QueueMessage(AzureQueueMessage {
                messages_url: Some(new_url),
                ..message
            }),
            m => m,
        }
    }

    pub fn id(&self) -> Uuid {
        match self {
            Message::QueueMessage(message) => message.message_id,
            // todo: add meaning full id if possible
            Message::LocalQueueMessage(_message) => Uuid::default(),
        }
    }
}
