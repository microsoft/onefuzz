// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use async_trait::async_trait;
use backoff::{self, future::retry_notify, ExponentialBackoff};
use onefuzz_telemetry::warn;
use reqwest::Response;
use std::{
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};

const DEFAULT_RETRY_PERIOD: Duration = Duration::from_secs(5);
const MAX_RETRY_ATTEMPTS: usize = 5;

pub async fn send_retry_reqwest_default<
    F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync,
>(
    build_request: F,
) -> Result<Response> {
    send_retry_reqwest(build_request, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS).await
}

pub async fn send_retry_reqwest<F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync>(
    build_request: F,
    retry_period: Duration,
    max_retry: usize,
) -> Result<Response> {
    let counter = AtomicUsize::new(0);
    let op = || async {
        let attempt_count = counter.fetch_add(1, Ordering::SeqCst);
        let request = build_request().map_err(backoff::Error::Permanent)?;
        let result = request
            .send()
            .await
            .with_context(|| format!("request attempt {} failed", attempt_count + 1));

        match result {
            Ok(x) => Ok(x),
            Err(x) => {
                if attempt_count >= max_retry {
                    return Err(backoff::Error::Permanent(x));
                } else {
                    return Err(backoff::Error::Transient(x));
                }
            }
        }
    };
    let result = retry_notify(
        ExponentialBackoff {
            current_interval: retry_period,
            initial_interval: retry_period,
            ..ExponentialBackoff::default()
        },
        op,
        |err, dur| warn!("request attempt failed after {:?}: {:?}", dur, err),
    )
    .await?;
    Ok(result)
}

#[async_trait]
pub trait SendRetry {
    async fn send_retry(self, retry_period: Duration, max_retry: usize) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS)
            .await
    }

    async fn send_retry(self, retry_period: Duration, max_retry: usize) -> Result<Response> {
        let result = send_retry_reqwest(
            || {
                self.try_clone().ok_or_else(|| {
                    anyhow::Error::msg("This request cannot be retried because it cannot be cloned")
                })
            },
            retry_period,
            max_retry,
        )
        .await?;

        Ok(result)
    }
}

#[cfg(test)]
mod test {
    use super::*;

    #[tokio::test]
    async fn retry_should_pass() -> Result<()> {
        reqwest::Client::new()
            .get("https://www.microsoft.com")
            .send_retry_default()
            .await?
            .error_for_status()?;

        Ok(())
    }

    #[tokio::test]
    async fn retry_should_fail() -> Result<()> {
        let invalid_url = "http://127.0.0.1:81/test.txt";
        let resp = reqwest::Client::new()
            .get(invalid_url)
            .send_retry(Duration::from_millis(1), 3)
            .await;

        if let Err(err) = &resp {
            let as_text = format!("{:?}", err);
            assert!(as_text.contains("request attempt 4 failed"));
        } else {
            anyhow::bail!("response to {} was expected to fail", invalid_url);
        }

        Ok(())
    }
}
