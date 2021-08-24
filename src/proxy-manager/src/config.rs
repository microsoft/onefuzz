// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::proxy;
use anyhow::Result;
use onefuzz_telemetry::{
    set_appinsights_clients, EventData, InstanceTelemetryKey, MicrosoftTelemetryKey, Role,
};
use reqwest_retry::SendRetry;
use serde::{Deserialize, Serialize};
use std::{fs::File, io::BufReader, path::PathBuf};
use storage_queue::QueueClient;
use thiserror::Error;
use url::Url;
use uuid::Uuid;

#[derive(Error, Debug)]
pub enum ProxyError {
    #[error("missing argument {0}")]
    MissingArg(String),

    #[error("missing etag header")]
    EtagError,

    #[error("unable to open config file")]
    FileError { source: std::io::Error },

    #[error("unable to parse config file")]
    ParseError { source: serde_json::error::Error },

    #[error(transparent)]
    Other {
        #[from]
        source: anyhow::Error,
    },
}

fn default_src_ip() -> String {
    "0.0.0.0".to_string()
}

#[derive(Debug, Deserialize, Serialize, PartialEq, Clone)]
pub struct Forward {
    #[serde(default = "default_src_ip")]
    pub src_ip: String,
    pub src_port: u16,
    pub dst_ip: String,
    pub dst_port: u16,
}

#[derive(Debug, Deserialize, Serialize, PartialEq, Clone)]
pub struct ConfigData {
    pub instance_id: Uuid,
    pub instance_telemetry_key: Option<InstanceTelemetryKey>,
    pub microsoft_telemetry_key: Option<MicrosoftTelemetryKey>,
    pub region: String,
    pub proxy_id: Uuid,
    pub url: Url,
    pub notification: Url,
    pub forwards: Vec<Forward>,
}

#[derive(Debug, Deserialize, Serialize, PartialEq)]
pub struct NotifyResponse<'a> {
    pub region: &'a str,
    pub proxy_id: Uuid,
    pub forwards: Vec<Forward>,
}

#[derive(Debug, Deserialize, Serialize)]
pub struct Config {
    config_path: PathBuf,
    data: ConfigData,
    etag: Option<String>,
}

impl Config {
    pub fn from_file(path: String) -> Result<Self> {
        let config_path = PathBuf::from(&path);

        let f = File::open(&config_path).map_err(|source| ProxyError::FileError { source })?;
        let r = BufReader::new(f);
        let data: ConfigData =
            serde_json::from_reader(r).map_err(|source| ProxyError::ParseError { source })?;

        set_appinsights_clients(
            data.instance_telemetry_key.clone(),
            data.microsoft_telemetry_key.clone(),
        );

        onefuzz_telemetry::set_property(EventData::Region(data.region.to_owned()));
        onefuzz_telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));
        onefuzz_telemetry::set_property(EventData::InstanceId(data.instance_id));
        onefuzz_telemetry::set_property(EventData::Role(Role::Proxy));

        Ok(Self {
            config_path,
            data,
            etag: None,
        })
    }

    async fn save(&self) -> Result<()> {
        let encoded = serde_json::to_string(&self.data)?;
        tokio::fs::write(&self.config_path, encoded).await?;
        Ok(())
    }

    async fn fetch(&mut self) -> Result<bool> {
        let mut request = reqwest::Client::new().get(self.data.url.clone());
        if let Some(etag) = &self.etag {
            request = request.header(reqwest::header::IF_NONE_MATCH, etag);
        }
        let response = request.send_retry_default().await?;
        let status = response.status();

        if status == reqwest::StatusCode::NOT_MODIFIED {
            return Ok(false);
        }

        if !status.is_success() {
            if status.is_server_error() {
                bail!("server error");
            } else {
                bail!("request failed: {:?}", status);
            }
        }

        let etag = response
            .headers()
            .get(reqwest::header::ETAG)
            .ok_or(ProxyError::EtagError)?
            .to_str()?
            .to_owned();
        let data: ConfigData = response.json().await?;
        self.etag = Some(etag);
        if data != self.data {
            self.data = data;
            Ok(true)
        } else {
            Ok(false)
        }
    }

    pub async fn notify(&self) -> Result<()> {
        info!("notifying service of proxy update");
        let client = QueueClient::new(self.data.notification.clone())?;

        client
            .enqueue(NotifyResponse {
                region: &self.data.region,
                proxy_id: self.data.proxy_id,
                forwards: self.data.forwards.clone(),
            })
            .await?;
        Ok(())
    }

    pub async fn update(&mut self) -> Result<()> {
        if self.fetch().await? {
            info!("config updated");
            self.save().await?;
            proxy::update(&self.data).await?;
            self.notify().await?;
        }

        Ok(())
    }
}
