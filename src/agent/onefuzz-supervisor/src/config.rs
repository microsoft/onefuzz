// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use reqwest::StatusCode;
use std::{
    path::Path,
    time::{Duration, Instant},
};

use anyhow::Result;
use url::Url;
use uuid::Uuid;

use crate::auth::{ClientCredentials, Credentials, ManagedIdentityCredentials};

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub struct StaticConfig {
    pub credentials: Credentials,

    pub pool_name: String,

    pub onefuzz_url: Url,

    pub instrumentation_key: Option<Uuid>,

    pub telemetry_key: Option<Uuid>,
}

// Temporary shim type to bridge the current service-provided config.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
struct RawStaticConfig {
    pub credentials: Option<ClientCredentials>,

    pub pool_name: String,

    pub onefuzz_url: Url,

    pub instrumentation_key: Option<Uuid>,

    pub telemetry_key: Option<Uuid>,
}

impl StaticConfig {
    pub fn new(data: &[u8]) -> Result<Self> {
        let config: RawStaticConfig = serde_json::from_slice(data)?;

        let credentials = match config.credentials {
            Some(client) => client.into(),
            None => {
                // Remove trailing `/`, which is treated as a distinct resource.
                let resource = config
                    .onefuzz_url
                    .to_string()
                    .trim_end_matches('/')
                    .to_owned();
                let managed = ManagedIdentityCredentials::new(resource);
                managed.into()
            }
        };
        let config = StaticConfig {
            credentials,
            pool_name: config.pool_name,
            onefuzz_url: config.onefuzz_url,
            instrumentation_key: config.instrumentation_key,
            telemetry_key: config.telemetry_key,
        };

        Ok(config)
    }

    pub async fn load(config_path: impl AsRef<Path>) -> Result<Self> {
        let data = tokio::fs::read(config_path).await?;
        Self::new(&data)
    }

    fn register_url(&self) -> Url {
        let mut url = self.onefuzz_url.clone();
        url.set_path("/api/agents/registration");
        url
    }
}

#[derive(Clone, Debug, Deserialize)]
pub struct DynamicConfig {
    /// Queried to get pending commands for the machine.
    pub commands_url: Url,

    /// Agent emits events to service by submitting here.
    pub events_url: Url,

    /// Work queue to poll, as an Azure Storage Queue SAS URL.
    pub work_queue: Url,
}

#[derive(Clone, Debug)]
pub struct Registration {
    pub config: StaticConfig,
    pub dynamic_config: DynamicConfig,
    pub machine_id: Uuid,
}

const DEFAULT_REGISTRATION_CREATE_TIMEOUT: Duration = Duration::from_secs(60 * 5);
const REGISTRATION_RETRY_PERIOD: Duration = Duration::from_secs(60);

impl Registration {
    pub async fn create(config: StaticConfig, managed: bool, timeout: Duration) -> Result<Self> {
        let token = config.credentials.access_token().await?;

        let machine_id = onefuzz::machine_id::get_machine_id().await?;

        let mut url = config.register_url();
        url.query_pairs_mut()
            .append_pair("machine_id", &machine_id.to_string())
            .append_pair("pool_name", &config.pool_name);

        if managed {
            let scaleset = onefuzz::machine_id::get_scaleset_name().await?;
            url.query_pairs_mut().append_pair("scaleset_id", &scaleset);
        }
        // The registration can fail because this call is made before the virtual machine scaleset is done provisioning
        // The authentication layer of the service will reject this request when that happens
        // We retry the registration here to mitigate this issue
        let end_time = Instant::now() + timeout;

        while Instant::now() < end_time {
            let response = reqwest::Client::new()
                .post(url.clone())
                .header("Content-Length", "0")
                .bearer_auth(&*token.secret())
                .body("")
                .send()
                .await?
                .error_for_status();

            match response {
                Ok(response) => {
                    let dynamic_config = response.json().await?;
                    return Ok(Self {
                        config,
                        dynamic_config,
                        machine_id,
                    });
                }
                Err(err) if err.status() == Some(StatusCode::UNAUTHORIZED) => {
                    warn!(
                        "Registration failed: {}\n retrying in {} seconds",
                        err,
                        REGISTRATION_RETRY_PERIOD.as_secs()
                    );
                    tokio::time::delay_for(REGISTRATION_RETRY_PERIOD).await;
                }
                Err(err) => return Err(err.into()),
            }
        }

        anyhow::bail!("Unable to register agent")
    }

    pub async fn create_managed(config: StaticConfig) -> Result<Self> {
        Self::create(config, true, DEFAULT_REGISTRATION_CREATE_TIMEOUT).await
    }

    pub async fn create_unmanaged(config: StaticConfig) -> Result<Self> {
        Self::create(config, false, DEFAULT_REGISTRATION_CREATE_TIMEOUT).await
    }

    pub async fn renew(&mut self) -> Result<()> {
        let token = self.config.credentials.access_token().await?;

        let machine_id = self.machine_id.to_string();

        let mut url = self.config.register_url();
        url.query_pairs_mut().append_pair("machine_id", &machine_id);

        let response = reqwest::Client::new()
            .get(url)
            .bearer_auth(&*token.secret())
            .send()
            .await?
            .error_for_status()?;

        self.dynamic_config = response.json().await?;

        Ok(())
    }
}
