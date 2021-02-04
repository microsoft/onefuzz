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

use crate::message::{AzureQueueMessage, Message, Receipt};

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
        let body = serde_xml_rs::to_string(&base64::encode(&serialized)).unwrap();
        let r = self
            .http
            .post(self.messages_url())
            .body(body)
            .send_retry_default()
            .await?;
        let _ = r.error_for_status()?;
        Ok(())
    }

    pub async fn pop(&mut self) -> Result<Option<AzureQueueMessage>> {
        let response = self
            .http
            .get(self.messages_url())
            .send_retry_default()
            .await?
            .error_for_status()?;
        let text = response.text().await?;
        let msg = parse_message(&text, self.messages_url.clone());

        let msg = if let Some(msg) = msg {
            msg
        } else {
            return Ok(None);
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
            .await?
            .error_for_status()?;
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

pub fn parse_message(text: &str, messages_url: Url) -> Option<AzureQueueMessage> {
    let message_id = parse_message_id(text)?;
    let pop_receipt = parse_pop_receipt(text)?;
    let receipt = Receipt {
        message_id,
        pop_receipt,
    };
    let data = parse_data(text)?;

    let msg = AzureQueueMessage {
        receipt,
        data,
        messages_url,
    };

    Some(msg)
}
