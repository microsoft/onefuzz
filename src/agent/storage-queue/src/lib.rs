// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);
pub mod azure_queue;
pub mod local_queue;
pub mod message;

use azure_queue::AzureQueueClient;
use message::Message;

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
