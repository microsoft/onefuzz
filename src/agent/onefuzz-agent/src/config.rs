// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use onefuzz::{
    auth::{ClientCredentials, Credentials, ManagedIdentityCredentials},
    http::{is_auth_error_code, ResponseExt},
    jitter::delay_with_jitter,
    machine_id::MachineIdentity,
};
use onefuzz_telemetry::{InstanceTelemetryKey, MicrosoftTelemetryKey};
use reqwest_retry::SendRetry;
use std::{
    path::{Path, PathBuf},
    time::{Duration, Instant},
};
use tokio::fs;
use url::Url;
use uuid::Uuid;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub struct StaticConfig {
    pub credentials: Credentials,

    pub pool_name: String,

    pub onefuzz_url: Url,

    pub multi_tenant_domain: Option<String>,

    pub instance_telemetry_key: Option<InstanceTelemetryKey>,

    pub microsoft_telemetry_key: Option<MicrosoftTelemetryKey>,

    pub heartbeat_queue: Option<Url>,

    pub job_result_queue: Option<Url>,

    pub instance_id: Uuid,

    #[serde(default = "default_as_true")]
    pub managed: bool,

    pub machine_identity: MachineIdentity,
}

fn default_as_true() -> bool {
    true
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub struct RawClientCredentials {
    client_id: Uuid,
    client_secret: String,
    tenant: String,
    multi_tenant_domain: Option<String>,
}

// Temporary shim type to bridge the current service-provided config.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
struct RawStaticConfig {
    pub client_credentials: Option<RawClientCredentials>,

    pub pool_name: String,

    pub onefuzz_url: Url,

    pub multi_tenant_domain: Option<String>,

    pub instance_telemetry_key: Option<InstanceTelemetryKey>,

    pub microsoft_telemetry_key: Option<MicrosoftTelemetryKey>,

    pub heartbeat_queue: Option<Url>,

    pub job_result_queue: Option<Url>,

    pub instance_id: Uuid,

    #[serde(default = "default_as_true")]
    pub managed: bool,

    pub machine_identity: Option<MachineIdentity>,
}

impl StaticConfig {
    pub async fn new(data: &[u8], machine_identity: Option<MachineIdentity>) -> Result<Self> {
        let config: RawStaticConfig = serde_json::from_slice(data)?;

        let credentials = match config.client_credentials {
            Some(client) => ClientCredentials::new(
                client.client_id,
                client.client_secret,
                config.onefuzz_url.to_string(),
                client.tenant,
                client.multi_tenant_domain,
            )
            .into(),
            None => {
                // Remove trailing `/`, which is treated as a distinct resource.
                let resource = config
                    .onefuzz_url
                    .to_string()
                    .trim_end_matches('/')
                    .to_owned();
                let managed =
                    ManagedIdentityCredentials::new(resource, config.multi_tenant_domain.clone())?;
                managed.into()
            }
        };
        let machine_identity = match machine_identity.or(config.machine_identity) {
            Some(machine_identity) => machine_identity,
            None => MachineIdentity::from_metadata().await?,
        };

        let config = StaticConfig {
            credentials,
            pool_name: config.pool_name,
            onefuzz_url: config.onefuzz_url,
            multi_tenant_domain: config.multi_tenant_domain,
            microsoft_telemetry_key: config.microsoft_telemetry_key,
            instance_telemetry_key: config.instance_telemetry_key,
            heartbeat_queue: config.heartbeat_queue,
            job_result_queue: config.job_result_queue,
            instance_id: config.instance_id,
            managed: config.managed,
            machine_identity,
        };

        Ok(config)
    }

    pub async fn from_file(
        config_path: impl AsRef<Path>,
        machine_identity: Option<MachineIdentity>,
    ) -> Result<Self> {
        let config_path = config_path.as_ref();
        let data = std::fs::read(config_path)
            .with_context(|| format!("unable to read config file: {}", config_path.display()))?;
        Self::new(&data, machine_identity).await
    }

    pub fn from_env() -> Result<Self> {
        let instance_id = Uuid::parse_str(&std::env::var("ONEFUZZ_INSTANCE_ID")?)?;
        let client_id = Uuid::parse_str(&std::env::var("ONEFUZZ_CLIENT_ID")?)?;
        let client_secret = std::env::var("ONEFUZZ_CLIENT_SECRET")?;
        let tenant = std::env::var("ONEFUZZ_TENANT")?;
        let multi_tenant_domain = std::env::var("ONEFUZZ_MULTI_TENANT_DOMAIN").ok();
        let onefuzz_url = Url::parse(&std::env::var("ONEFUZZ_URL")?)?;
        let pool_name = std::env::var("ONEFUZZ_POOL")?;
        let is_unmanaged = std::env::var("ONEFUZZ_IS_UNMANAGED").is_ok();
        let machine_identity = MachineIdentity::from_env()?;

        let heartbeat_queue = if let Ok(key) = std::env::var("ONEFUZZ_HEARTBEAT") {
            Some(Url::parse(&key)?)
        } else {
            None
        };

        let job_result_queue = if let Ok(key) = std::env::var("ONEFUZZ_JOB_RESULT") {
            Some(Url::parse(&key)?)
        } else {
            None
        };

        let instance_telemetry_key =
            if let Ok(key) = std::env::var("ONEFUZZ_INSTANCE_TELEMETRY_KEY") {
                Some(InstanceTelemetryKey::new(Uuid::parse_str(&key)?))
            } else {
                None
            };

        let microsoft_telemetry_key =
            if let Ok(key) = std::env::var("ONEFUZZ_MICROSOFT_TELEMETRY_KEY") {
                Some(MicrosoftTelemetryKey::new(Uuid::parse_str(&key)?))
            } else {
                None
            };

        let credentials = ClientCredentials::new(
            client_id,
            client_secret,
            onefuzz_url.to_string(),
            tenant,
            multi_tenant_domain.clone(),
        )
        .into();

        Ok(Self {
            credentials,
            pool_name,
            onefuzz_url,
            multi_tenant_domain,
            instance_telemetry_key,
            microsoft_telemetry_key,
            heartbeat_queue,
            job_result_queue,
            instance_id,
            managed: !is_unmanaged,
            machine_identity,
        })
    }

    fn register_url(&self) -> Url {
        let mut url = self.onefuzz_url.clone();
        url.set_path("/api/agents/registration");
        url
    }
}

#[derive(Clone, Debug, Deserialize, Serialize)]
pub struct DynamicConfig {
    /// Queried to get pending commands for the machine.
    pub commands_url: Url,

    /// Agent emits events to service by submitting here.
    pub events_url: Url,

    /// Work queue to poll, as an Azure Storage Queue SAS URL.
    pub work_queue: Url,
}

impl DynamicConfig {
    pub async fn save(&self, machine_id: Uuid) -> Result<()> {
        let path = Self::save_path(machine_id)?;
        let dir = path
            .parent()
            .ok_or(anyhow!("invalid dynamic config path"))?;
        fs::create_dir_all(dir).await?;
        let data = serde_json::to_vec(&self)?;
        fs::write(&path, &data)
            .await
            .with_context(|| format!("unable to save dynamic config: {}", path.display()))?;
        info!("saved dynamic-config: {}", path.display());
        Ok(())
    }

    pub async fn load(machine_id: Uuid) -> Result<Self> {
        let path = Self::save_path(machine_id)?;
        let data = fs::read(&path)
            .await
            .with_context(|| format!("unable to load dynamic config: {}", path.display()))?;
        let ctx: Self = serde_json::from_slice(&data)?;
        info!("loaded dynamic-config: {}", path.display());
        Ok(ctx)
    }

    fn save_path(machine_id: Uuid) -> Result<PathBuf> {
        Ok(onefuzz::fs::onefuzz_root()?
            .join("etc")
            .join(format!("dynamic-config-{machine_id}.json")))
    }
}

#[derive(Clone, Debug)]
pub struct Registration {
    pub config: StaticConfig,
    pub dynamic_config: DynamicConfig,
    pub machine_id: Uuid,
}

const DEFAULT_REGISTRATION_CREATE_TIMEOUT: Duration = Duration::from_secs(60 * 20);
const REGISTRATION_RETRY_PERIOD: Duration = Duration::from_secs(60);

impl Registration {
    pub async fn create(config: StaticConfig, managed: bool, timeout: Duration) -> Result<Self> {
        let token = config.credentials.access_token().await?;
        let machine_name = &config.machine_identity.machine_name;
        let machine_id = config.machine_identity.machine_id;

        let mut url = config.register_url();
        url.query_pairs_mut()
            .append_pair("machine_id", &machine_id.to_string())
            .append_pair("machine_name", machine_name)
            .append_pair("pool_name", &config.pool_name)
            .append_pair("version", env!("ONEFUZZ_VERSION"))
            .append_pair("os", std::env::consts::OS);

        if managed {
            match &config.machine_identity.scaleset_name {
                Some(scaleset) => {
                    url.query_pairs_mut().append_pair("scaleset_id", scaleset);
                }
                None => {
                    anyhow::bail!("managed instance without scaleset name");
                }
            }
        }
        // The registration can fail because this call is made before the virtual machine scaleset is done provisioning
        // The authentication layer of the service will reject this request when that happens
        // We retry the registration here to mitigate this issue
        let end_time = Instant::now() + timeout;

        while Instant::now() < end_time {
            let response = reqwest::Client::new()
                .post(url.clone())
                .header("Content-Length", "0")
                .bearer_auth(token.secret().expose_ref())
                .body("")
                .send_retry_default()
                .await
                .context("Registration.create")?;

            let status_code = response.status();

            match response.error_for_status_with_body().await {
                Ok(response) => {
                    let dynamic_config: DynamicConfig = response.json().await?;
                    dynamic_config.save(machine_id).await?;
                    return Ok(Self {
                        config,
                        dynamic_config,
                        machine_id,
                    });
                }
                Err(err) if is_auth_error_code(status_code) => {
                    warn!(
                        "Registration failed: {}\n retrying in {} seconds",
                        err,
                        REGISTRATION_RETRY_PERIOD.as_secs()
                    );
                    delay_with_jitter(REGISTRATION_RETRY_PERIOD).await;
                }
                Err(err) => return Err(err),
            }
        }

        anyhow::bail!("Unable to register agent")
    }

    pub async fn load_existing(config: StaticConfig) -> Result<Self> {
        let machine_id = config.machine_identity.machine_id;
        let dynamic_config = DynamicConfig::load(machine_id).await?;
        let registration = Self {
            config,
            dynamic_config,
            machine_id,
        };
        registration.renew().await
    }

    pub async fn create_managed(config: StaticConfig) -> Result<Self> {
        Self::create(config, true, DEFAULT_REGISTRATION_CREATE_TIMEOUT).await
    }

    pub async fn create_unmanaged(config: StaticConfig) -> Result<Self> {
        Self::create(config, false, DEFAULT_REGISTRATION_CREATE_TIMEOUT).await
    }

    pub async fn renew(&self) -> Result<Self> {
        info!("renewing registration");
        let token = self.config.credentials.access_token().await?;

        let machine_id = self.machine_id.to_string();

        let mut url = self.config.register_url();
        url.query_pairs_mut().append_pair("machine_id", &machine_id);

        let response = reqwest::Client::new()
            .get(url)
            .bearer_auth(token.secret().expose_ref())
            .send_retry_default()
            .await
            .context("Registration.renew")?
            .error_for_status_with_body()
            .await
            .context("Registration.renew request body")?;

        let dynamic_config: DynamicConfig = response.json().await?;
        dynamic_config.save(self.machine_id).await?;

        Ok(Self {
            dynamic_config,
            config: self.config.clone(),
            machine_id: self.machine_id,
        })
    }
}
