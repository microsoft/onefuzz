// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{bail, Result};
use async_trait::async_trait;
use reqwest::{Response, StatusCode};

#[async_trait]
pub trait ResponseExt: Sized {
    /// Alternative to `Response::error_for_status()` which includes the text of
    /// the response body, if it can be decoded.
    async fn error_for_status_with_body(self) -> Result<Self>;
}

#[async_trait]
impl ResponseExt for Response {
    async fn error_for_status_with_body(self) -> Result<Self> {
        let status = self.status();
        let is_err = status.is_client_error() || status.is_server_error();

        if is_err {
            let text = self.text().await;

            if let Ok(text) = text {
                bail!("{}: {}", status, text);
            } else {
                bail!("{}: <could not decode response body>", status);
            }
        }

        Ok(self)
    }
}

pub fn is_auth_error(err: &anyhow::Error) -> bool {
    if let Some(err) = err.downcast_ref::<reqwest::Error>() {
        if let Some(status) = err.status() {
            return is_auth_error_code(status);
        }
    }

    false
}

pub fn is_auth_error_code(status: StatusCode) -> bool {
    status == StatusCode::UNAUTHORIZED || status == StatusCode::FORBIDDEN
}
