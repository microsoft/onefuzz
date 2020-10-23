// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use backoff::{self, future::FutureOperation, ExponentialBackoff};
use reqwest::{Response, StatusCode};
use std::{
    sync::atomic::{AtomicI32, Ordering},
    time::Duration,
};

const DEFAULT_RETRY_PERIOD: Duration = Duration::from_millis(500);
const MAX_ELAPSED_TIME: Duration = Duration::from_secs(30);
const MAX_RETRY_ATTEMPTS: i32 = 5;

fn map_to_backoff_error(error: reqwest::Error) -> backoff::Error<anyhow::Error> {
    match error.status() {
        Some(StatusCode::REQUEST_TIMEOUT)
        | Some(StatusCode::TOO_MANY_REQUESTS)
        | Some(StatusCode::INTERNAL_SERVER_ERROR)
        | Some(StatusCode::BAD_GATEWAY)
        | Some(StatusCode::SERVICE_UNAVAILABLE)
        | Some(StatusCode::GATEWAY_TIMEOUT) => {
            backoff::Error::Transient(anyhow::Error::from(error))
        }
        _ => backoff::Error::Permanent(anyhow::Error::from(error)),
    }
}

#[async_trait]
pub trait SendRetry {
    async fn send_retry<F: Fn(reqwest::Error) -> backoff::Error<anyhow::Error> + Send + Sync>(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
        error_mapper: F,
    ) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(DEFAULT_RETRY_PERIOD, MAX_ELAPSED_TIME, map_to_backoff_error)
            .await
    }

    async fn send_retry<F: Fn(reqwest::Error) -> backoff::Error<anyhow::Error> + Send + Sync>(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
        error_mapper: F,
    ) -> Result<Response> {
        let counter = AtomicI32::new(0);
        let op = || async {
            if (counter.fetch_add(1, Ordering::SeqCst) >= MAX_RETRY_ATTEMPTS) {
                Result::<Response, backoff::Error<anyhow::Error>>::Err(backoff::Error::Permanent(
                    anyhow::Error::msg("Maximum number of attemps reached for this request"),
                ))
            } else {
                let response = self
                    .try_clone()
                    .ok_or_else(|| {
                        backoff::Error::Permanent(anyhow::Error::msg(
                            "this request cannot be cloned",
                        ))
                    })?
                    .send()
                    .await
                    .map_err(|err| error_mapper(err))?;

                Result::<Response, backoff::Error<anyhow::Error>>::Ok(response)
            }
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
