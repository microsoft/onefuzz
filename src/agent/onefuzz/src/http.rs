use anyhow::{bail, Result};
use async_trait::async_trait;
use reqwest::Response;


#[async_trait]
pub trait ResponseExt: Sized {
    /// Alternative to `Response::error_for_status()` which includes the text of
    /// the response body.
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
                // Couldn't decode HTTP response body.
                bail!("{}: <could not decode response body>", status);
            }
        }

        Ok(self)
    }
}