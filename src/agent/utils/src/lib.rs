use anyhow::Result;
use async_trait::async_trait;
use backoff::{self, future::FutureOperation, ExponentialBackoff};
use reqwest::Response;
use std::time::Duration;
use log;

const DEFAULT_RETRY_PERIOD: Duration = Duration::from_millis(500);
const MAX_ELAPSED_TIME: Duration = Duration::from_secs(60 * 5);

#[async_trait]
pub trait SendRetry {
    async fn send_retry(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
    ) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(DEFAULT_RETRY_PERIOD, MAX_ELAPSED_TIME)
            .await
    }

    async fn send_retry(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
    ) -> Result<Response> {
        let op = || async {
            let cloned = self
                .try_clone()
                .ok_or(backoff::Error::Permanent(anyhow::Error::msg(
                    "this request cannot be cloned",
                )))?;

            let response = cloned
                .send()
                .await
                .map_err(|err| backoff::Error::Permanent(anyhow::Error::from(err)))?
                .error_for_status()
                .map_err(|err| {
                    log::warn!("Transient error {}", err);
                    backoff::Error::Transient(anyhow::Error::from(err))
                })?;
            Result::<Response, backoff::Error<anyhow::Error>>::Ok(response)
        };

        let result = op
            .retry(ExponentialBackoff {
                current_interval: retry_period,
                initial_interval: retry_period,
                max_elapsed_time: Some(max_elapsed_time),
                ..ExponentialBackoff::default()
            })
            .await?;

        Ok(result)
    }
}
