// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Context, Result};
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::time::Duration;
use uuid::Uuid;

pub const EMPTY_QUEUE_DELAY: Duration = Duration::from_secs(10);

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "PascalCase")]
struct QueueMessage {
    message_text: Option<String>,
}

pub struct QueueClient {
    http: Client,
    messages_url: Url,
}

impl QueueClient {
    pub fn new(queue_url: Url) -> Self {
        let http = Client::new();

        let messages_url = {
            let queue_path = queue_url.path();
            let messages_path = format!("{}/messages", queue_path);
            let mut url = queue_url;
            url.set_path(&messages_path);
            url
        };

        Self { http, messages_url }
    }

    pub async fn enqueue(&self, data: impl Serialize) -> Result<()> {
        let serialized = serde_json::to_string(&data).unwrap();
        let queue_message = QueueMessage {
            message_text: Some(base64::encode(&serialized)),
        };
        let body = serde_xml_rs::to_string(&queue_message).unwrap();
        let r = self
            .http
            .post(self.messages_url())
            .body(body)
            .send_retry_default()
            .await
            .context("storage queue enqueue failed")?;
        let _ = r
            .error_for_status()
            .context("storage queue enqueue failed with error")?;
        Ok(())
    }

    pub async fn pop(&mut self) -> Result<Option<Message>> {
        let response = self
            .http
            .get(self.messages_url())
            .send_retry_default()
            .await
            .context("storage queue pop failed")?
            .error_for_status()
            .context("storage queue pop failed with error")?;
        let text = response
            .text()
            .await
            .context("unable to parse response text")?;
        let msg = Message::parse(&text);

        let msg = if let Some(msg) = msg {
            msg
        } else {
            if is_empty_message(&text) {
                return Ok(None);
            }
            bail!("unable to parse response text body: {}", text);
        };

        let msg = if msg.data.is_empty() { None } else { Some(msg) };

        Ok(msg)
    }

    pub async fn delete(&mut self, receipt: impl Into<Receipt>) -> Result<()> {
        let receipt = receipt.into();
        let url = self.delete_url(receipt);
        self.http
            .delete(url)
            .send_retry_default()
            .await
            .context("storage queue delete failed")?
            .error_for_status()
            .context("storage queue delete failed")?;
        Ok(())
    }

    fn delete_url(&self, receipt: Receipt) -> Url {
        let messages_url = self.messages_url();
        let messages_path = messages_url.path();
        let item_path = format!("{}/{}", messages_path, receipt.message_id);
        let mut url = messages_url;
        url.set_path(&item_path);
        url.query_pairs_mut()
            .append_pair("popreceipt", &receipt.pop_receipt);
        url
    }

    fn messages_url(&self) -> Url {
        self.messages_url.clone()
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Receipt {
    // Unique ID of the associated queue message.
    pub message_id: Uuid,

    // Opaque data that licenses message deletion.
    pub pop_receipt: String,
}

impl From<Message> for Receipt {
    fn from(msg: Message) -> Self {
        msg.receipt
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Message {
    pub receipt: Receipt,
    pub data: Vec<u8>,
}

impl Message {
    pub fn id(&self) -> Uuid {
        self.receipt.message_id
    }

    pub fn data(&self) -> &[u8] {
        &self.data
    }

    fn parse(text: &str) -> Option<Message> {
        let message_id = parse_message_id(text)?;
        let pop_receipt = parse_pop_receipt(text)?;
        let receipt = Receipt {
            message_id,
            pop_receipt,
        };
        let data = parse_data(text)?;

        let msg = Self { receipt, data };

        Some(msg)
    }

    pub fn get<'a, T: serde::de::Deserialize<'a>>(&'a self) -> Result<T> {
        let data =
            serde_json::from_slice(&self.data).context("get storage queue message failed")?;
        Ok(data)
    }
}

fn is_empty_message(text: &str) -> bool {
    regex::Regex::new(r".*<QueueMessagesList>[\s\n\r]*</QueueMessagesList>")
        .unwrap()
        .is_match(&text)
        || text.contains(r"<QueueMessagesList />")
}

fn parse_message_id(text: &str) -> Option<Uuid> {
    let pat = r"<MessageId>(.*)</MessageId>";
    let re = regex::Regex::new(pat).unwrap();

    let msg_id_text = re.captures_iter(text).next()?.get(1)?.as_str();

    Uuid::parse_str(msg_id_text).ok()
}

fn parse_pop_receipt(text: &str) -> Option<String> {
    let pat = r"<PopReceipt>(.*)</PopReceipt>";
    let re = regex::Regex::new(pat).unwrap();

    let text = re.captures_iter(text).next()?.get(1)?.as_str().into();

    Some(text)
}

fn parse_data(text: &str) -> Option<Vec<u8>> {
    let pat = r"<MessageText>(.*)</MessageText>";
    let re = regex::Regex::new(pat).unwrap();

    let encoded = re.captures_iter(text).next()?.get(1)?.as_str();
    let decoded = base64::decode(encoded).ok()?;

    Some(decoded)
}
