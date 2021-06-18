// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use bytes::Buf;
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);

// <QueueMessagesList>
// 	<QueueMessage>
// 		<MessageId>7d35e47d-f58e-42da-ba4a-9e6ac7e1214d</MessageId>
// 		<InsertionTime>Fri, 05 Feb 2021 06:27:47 GMT</InsertionTime>
// 		<ExpirationTime>Fri, 12 Feb 2021 06:27:47 GMT</ExpirationTime>
// 		<PopReceipt>AgAAAAMAAAAAAAAAtg40eYj71gE=</PopReceipt>
// 		<TimeNextVisible>Fri, 05 Feb 2021 06:31:02 GMT</TimeNextVisible>
// 		<DequeueCount>1</DequeueCount>
// 		<MessageText>dGVzdA==</MessageText>
// 	</QueueMessage>
// </QueueMessagesList>

// #[derive(Derivative)]
#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(rename = "QueueMessage")]
pub struct AzureQueueMessage {
    pub message_id: Uuid,
    // InsertionTime:
    // ExpirationTime
    pub pop_receipt: String,
    // TimeNextVisible
    // DequeueCount
    pub message_text: String,

    #[serde(skip)]
    pub messages_url: Option<Url>,
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(rename = "QueueMessage")]
pub struct AzureQueueMessageSend {
    pub message_text: String,
}

impl AzureQueueMessage {
    pub fn parse<T>(&self, parser: impl FnOnce(&[u8]) -> Result<T>) -> Result<T> {
        let decoded = base64::decode(&self.message_text)?;
        parser(&decoded)
    }

    pub async fn claim<T: DeserializeOwned>(self) -> Result<T> {
        if let Some(messages_url) = self.messages_url {
            let messages_path = messages_url.path();
            let item_path = format!("{}/{}", messages_path, self.message_id);
            let mut url = messages_url.clone();
            url.set_path(&item_path);
            url.query_pairs_mut()
                .append_pair("popreceipt", &self.pop_receipt);

            let http = Client::new();
            http.delete(url)
                .send_retry_default()
                .await
                .context("AzureQueueMessage.claim")?
                .error_for_status()
                .context("AzureQueueMessage.claim status body")?;
        }
        let decoded = base64::decode(self.message_text)?;
        let value: T = serde_json::from_slice(&decoded)?;
        Ok(value)
    }
    pub async fn delete(&self) -> Result<()> {
        if let Some(messages_url) = self.messages_url.clone() {
            let messages_path = messages_url.path();
            let item_path = format!("{}/{}", messages_path, self.message_id);
            let mut url = messages_url.clone();
            url.set_path(&item_path);
            url.query_pairs_mut()
                .append_pair("popreceipt", &self.pop_receipt);

            let http = Client::new();
            http.delete(url)
                .send_retry_default()
                .await
                .context("storage queue delete failed")?
                .error_for_status()
                .context("storage queue delete failed")?;
        }

        Ok(())
    }

    pub fn get<T: DeserializeOwned>(&self) -> Result<T> {
        let decoded = base64::decode(&self.message_text)?;
        let value = serde_json::from_slice(&decoded)?;
        Ok(value)
    }
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(rename = "QueueMessagesList")]
struct AzureQueueMessageList {
    #[serde(rename = "QueueMessage", default)]
    pub queue_message: Option<AzureQueueMessage>,
}

#[derive(Debug, Clone)]
pub struct AzureQueueClient {
    pub http: Client,
    pub messages_url: Url,
}

impl AzureQueueClient {
    pub fn new(queue_url: Url) -> Self {
        let http = Client::new();
        let messages_url = {
            let queue_path = queue_url.path();
            let messages_path = format!("{}/messages", queue_path);
            let mut url = queue_url;
            url.set_path(&messages_path);
            url
        };
        AzureQueueClient { http, messages_url }
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        let serialized = serde_json::to_string(&data).unwrap();
        let body = serde_xml_rs::to_string(&AzureQueueMessageSend {
            message_text: base64::encode(&serialized),
        })?;
        self.http
            .post(self.messages_url.clone())
            .body(body)
            .send_retry_default()
            .await
            .context("storage queue enqueue failed")?
            .error_for_status()
            .context("storage queue enqueue failed with error")?;
        Ok(())
    }

    pub async fn pop(&self) -> Result<Option<AzureQueueMessage>> {
        let response = self
            .http
            .get(self.messages_url.clone())
            .send_retry_default()
            .await
            .context("storage queue pop failed")?
            .error_for_status()
            .context("storage queue pop failed with error")?;

        let buf = {
            let buf = response.bytes().await?;
            //remove the byte order mark if present
            if buf.slice(0..3).to_vec() == [0xef, 0xbb, 0xbf] {
                buf.slice(3..)
            } else {
                buf
            }
        };

        let msg: AzureQueueMessageList = serde_xml_rs::from_reader(buf.reader())?;

        let m = msg.queue_message.map(|msg| AzureQueueMessage {
            messages_url: Some(self.messages_url.clone()),
            ..msg
        });
        Ok(m)
    }
}
