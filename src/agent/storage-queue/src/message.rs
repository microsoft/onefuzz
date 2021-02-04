// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use async_trait::async_trait;
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum Message {
    QueueMessage(AzureQueueMessage),
}
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct AzureQueueMessage {
    pub messages_url: Url,
    pub receipt: Receipt,
    pub data: Vec<u8>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Receipt {
    // Unique ID of the associated queue message.
    pub message_id: Uuid,

    // Opaque data that licenses message deletion.
    pub pop_receipt: String,
}

impl Message {
    pub fn get<'a, T: serde::de::Deserialize<'a>>(&'a self) -> Result<T> {
        match self {
            Message::QueueMessage(message) => {
                let data = serde_json::from_slice(&message.data)?;
                Ok(data)
            }
        }
    }

    pub fn data(&self) -> &[u8] {
        match self {
            Message::QueueMessage(message) => &message.data,
        }
    }

    pub async fn delete(&self) -> Result<()> {
        match self {
            Message::QueueMessage(message) => {
                let receipt = &message.receipt;
                let messages_url = &message.messages_url;
                let messages_path = messages_url.path();
                let item_path = format!("{}/{}", messages_path, receipt.message_id);
                let mut url = messages_url.clone();
                url.set_path(&item_path);
                url.query_pairs_mut()
                    .append_pair("popreceipt", &receipt.pop_receipt);

                let http = Client::new();
                http.delete(url)
                    .send_retry_default()
                    .await?
                    .error_for_status()?;
                Ok(())
            }
        }
    }

    pub fn id(&self) -> Uuid {
        match self {
            Message::QueueMessage(message) => message.receipt.message_id,
        }
    }
}
