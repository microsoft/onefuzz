use anyhow::Result;
use async_trait::async_trait;
use backoff::{future::FutureOperation as _, ExponentialBackoff};
use reqwest::Response;
use std::time::Duration;
const DEFAULT_RETRY_PERIOD: Duration = Duration::from_millis(500);

#[async_trait]
pub trait SendRetry {
    async fn send_retry(self, retry_period: Duration) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(DEFAULT_RETRY_PERIOD).await
    }

    async fn send_retry(self, retry_period: Duration) -> Result<Response> {
        match self.try_clone() {
            Some(cloned) => Ok((|| async { Ok(cloned.try_clone().unwrap().send().await?) })
                .retry(ExponentialBackoff {
                    current_interval: retry_period,
                    initial_interval: retry_period,
                    ..ExponentialBackoff::default()
                })
                .await?),
            None => Ok(self.send().await?),
        }
    }
}
