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

const DEFAULT_RETRY_PERIOD: Duration = Duration::from_secs(2);
const MAX_ELAPSED_TIME: Duration = Duration::from_secs(30);
const MAX_RETRY_ATTEMPTS: i32 = 5;

fn to_backoff_response(
    result: Result<Response, reqwest::Error>,
) -> Result<Response, backoff::Error<anyhow::Error>> {
    match result {
        Err(error) => Err(backoff::Error::Permanent(anyhow::Error::from(error))),
        Ok(response) => response
            .error_for_status()
            .map_err(|error| match error.status() {
                Some(StatusCode::REQUEST_TIMEOUT)
                | Some(StatusCode::TOO_MANY_REQUESTS)
                | Some(StatusCode::INTERNAL_SERVER_ERROR)
                | Some(StatusCode::BAD_GATEWAY)
                | Some(StatusCode::SERVICE_UNAVAILABLE)
                | Some(StatusCode::GATEWAY_TIMEOUT) => {
                    log::warn!("Transient error: {}", error);
                    backoff::Error::Transient(anyhow::Error::from(error))
                }
                _ => {
                    log::warn!("Permanent error: {}", error);
                    backoff::Error::Permanent(anyhow::Error::from(error))
                }
            }),
    }
}

pub async fn send_retry_reqwest_default<
    F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync,
>(
    build_request: F,
) -> Result<Response> {
    send_retry_reqwest(
        build_request,
        DEFAULT_RETRY_PERIOD,
        MAX_ELAPSED_TIME,
        MAX_RETRY_ATTEMPTS,
        to_backoff_response,
    )
    .await
}

pub async fn send_retry_reqwest<
    F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync,
    F2: Fn(Result<Response, reqwest::Error>) -> Result<Response, backoff::Error<anyhow::Error>>
        + Send
        + Sync,
>(
    build_request: F,
    retry_period: Duration,
    max_elapsed_time: Duration,
    max_retry: i32,
    error_mapper: F2,
) -> Result<Response> {
    let counter = AtomicI32::new(0);
    let op = || async {
        if counter.fetch_add(1, Ordering::SeqCst) >= max_retry {
            Result::<Response, backoff::Error<anyhow::Error>>::Err(backoff::Error::Permanent(
                anyhow::Error::msg("Maximum number of attempts reached for this request"),
            ))
        } else {
            let request = build_request().map_err(backoff::Error::Permanent)?;
            let response = request.send().await;
            Result::<Response, backoff::Error<anyhow::Error>>::Ok(error_mapper(response)?)
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

#[async_trait]
pub trait SendRetry {
    async fn send_retry<
        F: Fn(Result<Response, reqwest::Error>) -> Result<Response, backoff::Error<anyhow::Error>>
            + Send
            + Sync,
    >(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
        max_retry: i32,
        error_mapper: F,
    ) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(
            DEFAULT_RETRY_PERIOD,
            MAX_ELAPSED_TIME,
            MAX_RETRY_ATTEMPTS,
            to_backoff_response,
        )
        .await
    }

    async fn send_retry<
        F: Fn(Result<Response, reqwest::Error>) -> Result<Response, backoff::Error<anyhow::Error>>
            + Send
            + Sync,
    >(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
        max_retry: i32,
        response_mapper: F,
    ) -> Result<Response> {
        let result = send_retry_reqwest(
            || {
                self.try_clone().ok_or_else(|| {
                    anyhow::Error::msg("This request cannot be retried because it cannot be cloned")
                })
            },
            retry_period,
            max_elapsed_time,
            max_retry,
            response_mapper,
        )
        .await?;

        Ok(result)
    }
}
