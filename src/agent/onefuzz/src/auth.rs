// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fmt;

use crate::http::ResponseExt;
use anyhow::Result;
use reqwest_retry::SendRetry;
use url::Url;
use uuid::Uuid;

#[derive(Clone, Deserialize, Eq, PartialEq, Serialize)]
pub struct Secret<T>(T);

impl<T> Secret<T> {
    pub fn expose(self) -> T {
        self.0
    }

    pub fn expose_ref(&self) -> &T {
        &self.0
    }

    pub fn expose_mut(&mut self) -> &mut T {
        &mut self.0
    }
}

impl<T> From<T> for Secret<T> {
    fn from(data: T) -> Self {
        Secret(data)
    }
}

impl<T> fmt::Debug for Secret<T> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "Secret(<REDACTED>)")
    }
}

impl<T> fmt::Display for Secret<T> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "<REDACTED>")
    }
}

#[derive(Clone, Deserialize, Eq, PartialEq)]
pub struct AccessToken {
    secret: Secret<String>,
}

impl AccessToken {
    pub fn secret(&self) -> &Secret<String> {
        &self.secret
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub enum Credentials {
    Client(ClientCredentials),
    ManagedIdentity(ManagedIdentityCredentials),
}

impl Credentials {
    pub async fn access_token(&self) -> Result<AccessToken> {
        match self {
            Credentials::Client(credentials) => credentials.access_token().await,
            Credentials::ManagedIdentity(credentials) => credentials.access_token().await,
        }
    }
}

impl From<ClientCredentials> for Credentials {
    fn from(credentials: ClientCredentials) -> Self {
        Credentials::Client(credentials)
    }
}

impl From<ManagedIdentityCredentials> for Credentials {
    fn from(credentials: ManagedIdentityCredentials) -> Self {
        Credentials::ManagedIdentity(credentials)
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub struct ClientCredentials {
    client_id: Uuid,
    client_secret: Secret<String>,
    resource: String,
    tenant: String,
}

impl ClientCredentials {
    pub fn new(client_id: Uuid, client_secret: String, resource: String, tenant: String) -> Self {
        let client_secret = client_secret.into();

        Self {
            client_id,
            client_secret,
            resource,
            tenant,
        }
    }

    pub async fn access_token(&self) -> Result<AccessToken> {
        let mut url = Url::parse("https://login.microsoftonline.com")?;
        url.path_segments_mut()
            .expect("Authority URL is cannot-be-a-base")
            .extend(&[&self.tenant, "oauth2", "v2.0", "token"]);

        let response = reqwest::Client::new()
            .post(url)
            .header("Content-Length", "0")
            .form(&[
                ("client_id", self.client_id.to_hyphenated().to_string()),
                ("client_secret", self.client_secret.expose_ref().to_string()),
                ("grant_type", "client_credentials".into()),
                ("tenant", self.tenant.clone()),
                ("scope", format!("{}.default", self.resource)),
            ])
            .send_retry_default()
            .await?
            .error_for_status_with_body()
            .await?;

        let body: ClientAccessTokenBody = response.json().await?;

        Ok(body.into())
    }
}

// See: https://docs.microsoft.com/en-us/azure/active-directory/develop
//      /v2-oauth2-client-creds-grant-flow#successful-response-1
#[derive(Clone, Debug, Deserialize)]
struct ClientAccessTokenBody {
    // Bearer token for authenticating HTTP requests.
    access_token: Secret<String>,
}

impl From<ClientAccessTokenBody> for AccessToken {
    fn from(body: ClientAccessTokenBody) -> Self {
        let secret = body.access_token;
        AccessToken { secret }
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
pub struct ManagedIdentityCredentials {
    resource: String,
}

const MANAGED_IDENTITY_URL: &str =
    "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01";

impl ManagedIdentityCredentials {
    pub fn new(resource: String) -> Self {
        Self { resource }
    }

    fn url(&self) -> Url {
        let mut url = Url::parse(MANAGED_IDENTITY_URL).unwrap();
        url.query_pairs_mut()
            .append_pair("resource", &self.resource);
        url
    }

    pub async fn access_token(&self) -> Result<AccessToken> {
        let response = reqwest::Client::new()
            .get(self.url())
            .header("Metadata", "true")
            .send_retry_default()
            .await?
            .error_for_status_with_body()
            .await?;

        let body: ManagedIdentityAccessTokenBody = response.json().await?;

        Ok(body.into())
    }
}

// Note: this is a _subset_ of the actual response body. Don't deserialize
// fields we don't use.
//
// See: https://docs.microsoft.com/en-us/azure/active-directory
//      /managed-identities-azure-resources/tutorial-linux-vm-access-arm
//      #get-an-access-token-using-the-vms-system-assigned-managed-identity
//      -and-use-it-to-call-resource-manager
#[derive(Clone, Debug, Deserialize)]
struct ManagedIdentityAccessTokenBody {
    access_token: Secret<String>,
    resource: String,
}

impl From<ManagedIdentityAccessTokenBody> for AccessToken {
    fn from(body: ManagedIdentityAccessTokenBody) -> Self {
        let secret = body.access_token;
        AccessToken { secret }
    }
}
