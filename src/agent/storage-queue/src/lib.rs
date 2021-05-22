// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{anyhow, Context, Result};
use reqwest::Url;
use serde::{de::DeserializeOwned, Deserialize, Deserializer, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);
pub mod azure_queue;
pub mod local_queue;

use azure_queue::{AzureQueueClient, AzureQueueMessage};
use local_queue::{ChannelQueueClient, FileQueueClient, LocalQueueMessage};

#[derive(Debug, Clone)]
pub enum QueueClient {
    AzureQueue(AzureQueueClient),
    FileQueueClient(Box<FileQueueClient>),
    Channel(ChannelQueueClient),
}

impl<'de> Deserialize<'de> for QueueClient {
    fn deserialize<D>(deserializer: D) -> std::result::Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        Url::deserialize(deserializer)
            .map(QueueClient::new)?
            .map_err(serde::de::Error::custom)
    }
}

impl QueueClient {
    pub fn new(queue_url: Url) -> Result<Self> {
        if queue_url.scheme().to_lowercase() == "file" {
            let path = queue_url
                .to_file_path()
                .map_err(|_| anyhow!("invalid local path"))?;
            let local_queue = FileQueueClient::new(path)?;
            Ok(QueueClient::FileQueueClient(Box::new(local_queue)))
        } else {
            Ok(QueueClient::AzureQueue(AzureQueueClient::new(queue_url)))
        }
    }

    pub fn get_url(self) -> Result<Url> {
        match self {
            QueueClient::AzureQueue(queue_client) => Ok(queue_client.messages_url),
            QueueClient::FileQueueClient(queue_client) => {
                Url::from_file_path(queue_client.as_ref().path.clone())
                    .map_err(|_| anyhow!("invalid queue url"))
            }
            QueueClient::Channel(queue_client) => Ok(queue_client.url),
        }
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        match self {
            QueueClient::AzureQueue(queue_client) => queue_client.enqueue(data).await,
            QueueClient::FileQueueClient(queue_client) => queue_client.enqueue(data).await,
            QueueClient::Channel(queue_client) => queue_client.enqueue(data).await,
        }
        .context("QueueClient.enqueue")
    }

    pub async fn pop(&self) -> Result<Option<Message>> {
        match self {
            QueueClient::AzureQueue(queue_client) => {
                let message = queue_client.pop().await.context("QueueClient.pop")?;
                Ok(message.map(Message::QueueMessage))
            }
            QueueClient::FileQueueClient(queue_client) => {
                let message = queue_client.pop().await.context("QueueClient.pop")?;
                Ok(message.map(Message::LocalQueueMessage))
            }
            QueueClient::Channel(queue_client) => {
                let message = queue_client.pop().await.context("QueueClient.pop")?;
                Ok(message.map(Message::LocalQueueMessage))
            }
        }
    }
}

#[derive(Debug)]
pub enum Message {
    QueueMessage(AzureQueueMessage),
    LocalQueueMessage(LocalQueueMessage),
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
            Message::LocalQueueMessage(_) => Ok(()),
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
            Message::LocalQueueMessage(_message) => Uuid::default(),
        }
    }
}
