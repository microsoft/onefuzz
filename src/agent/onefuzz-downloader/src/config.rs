// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use onefuzz::auth::{ClientCredentials, Credentials, ManagedIdentityCredentials};
use std::path::Path;
use url::Url;
use uuid::Uuid;

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

    pub fn from_env() -> Result<Self> {
        let client_id = Uuid::parse_str(&std::env::var("ONEFUZZ_CLIENT_ID")?)?;
        let client_secret = std::env::var("ONEFUZZ_CLIENT_SECRET")?.into();
        let tenant = std::env::var("ONEFUZZ_TENANT")?;
        let onefuzz_url = Url::parse(&std::env::var("ONEFUZZ_URL")?)?;
        let pool_name = std::env::var("ONEFUZZ_POOL")?;

        let instrumentation_key = if let Ok(key) = std::env::var("ONEFUZZ_INSTRUMENTATION_KEY") {
            Some(Uuid::parse_str(&key)?)
        } else {
            None
        };

        let telemetry_key = if let Ok(key) = std::env::var("ONEFUZZ_TELEMETRY_KEY") {
            Some(Uuid::parse_str(&key)?)
        } else {
            None
        };

        let credentials = ClientCredentials::new(
            client_id,
            client_secret,
            onefuzz_url.clone().to_string(),
            tenant,
        )
        .into();

        Ok(Self {
            credentials,
            pool_name,
            onefuzz_url,
            instrumentation_key,
            telemetry_key,
        })
    }

    pub fn from_file(config_path: impl AsRef<Path>) -> Result<Self> {
        verbose!("loading config from: {}", config_path.as_ref().display());
        let data = std::fs::read(config_path)?;
        Self::new(&data)
    }

    pub fn download_url(&self) -> Url {
        let mut url = self.onefuzz_url.clone();
        url.set_path("/api/agents/download");
        url
    }
}
