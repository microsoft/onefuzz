// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use async_trait::async_trait;
use backoff::{self, future::retry_notify, ExponentialBackoff};
use onefuzz_telemetry::debug;
use reqwest::{Response, StatusCode};
use std::{
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};

pub const DEFAULT_RETRY_PERIOD: Duration = Duration::from_secs(5);
pub const MAX_RETRY_ATTEMPTS: usize = 5;

pub enum RetryCheck {
    Retry,
    Fail,
    Succeed,
}

fn always_retry(_: StatusCode) -> RetryCheck {
    RetryCheck::Retry
}

pub async fn send_retry_reqwest_default<
    F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync,
>(
    build_request: F,
) -> Result<Response> {
    send_retry_reqwest(
        build_request,
        |_| RetryCheck::Retry,
        DEFAULT_RETRY_PERIOD,
        MAX_RETRY_ATTEMPTS,
    )
    .await
}

pub async fn send_retry_reqwest<F, R>(
    build_request: F,
    check_status: R,
    retry_period: Duration,
    max_retry: usize,
) -> Result<Response>
where
    F: Fn() -> Result<reqwest::RequestBuilder> + Send + Sync,
    R: Fn(StatusCode) -> RetryCheck + Send + Sync,
{
    let counter = AtomicUsize::new(0);
    let op = || async {
        let attempt_count = counter.fetch_add(1, Ordering::SeqCst);
        let request = build_request().map_err(|err| backoff::Error::Permanent(Err(err)))?;
        let result = request
            .send()
            .await
            .with_context(|| format!("request attempt {} failed", attempt_count + 1));

        match result {
            Err(x) => {
                if attempt_count >= max_retry {
                    Err(backoff::Error::Permanent(Err(x)))
                } else {
                    Err(backoff::Error::Transient(Err(x)))
                }
            }
            Ok(x) => {
                if x.status().is_success() {
                    Ok(x)
                } else {
                    let status = x.status();
                    let result = check_status(status);

                    match result {
                        RetryCheck::Succeed => Ok(x),
                        RetryCheck::Fail => {
                            match x.error_for_status().with_context(|| {
                                format!("request attempt {} failed", attempt_count + 1)
                            }) {
                                // the is_success check earlier should have taken care of this already.
                                Ok(x) => Ok(x),
                                Err(as_err) => Err(backoff::Error::Permanent(Err(as_err))),
                            }
                        }
                        RetryCheck::Retry => {
                            match x.error_for_status().with_context(|| {
                                format!("request attempt {} failed", attempt_count + 1)
                            }) {
                                // the is_success check earlier should have taken care of this already.
                                Ok(x) => Ok(x),
                                Err(as_err) => {
                                    if attempt_count >= max_retry {
                                        Err(backoff::Error::Permanent(Err(as_err)))
                                    } else {
                                        Err(backoff::Error::Transient(Err(as_err)))
                                    }
                                }
                            }
                        }
                    }
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
        |err: Result<Response, anyhow::Error>, dur| match err {
            Ok(response) => {
                if let Err(err) = response.error_for_status() {
                    debug!("request attempt failed after {:?}: {:?}", dur, err)
                }
            }
            err => debug!("request attempt failed after {:?}: {:?}", dur, err),
        },
    )
    .await;

    match result {
        Ok(response) | Err(Ok(response)) => Ok(response),
        Err(Err(err)) => Err(err),
    }
}

#[async_trait]
pub trait SendRetry {
    async fn send_retry<R>(
        self,
        check_status: R,
        retry_period: Duration,
        max_retry: usize,
    ) -> Result<Response>
    where
        R: Fn(StatusCode) -> RetryCheck + Send + Sync;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(always_retry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS)
            .await
    }

    async fn send_retry<R>(
        self,
        check_status: R,
        retry_period: Duration,
        max_retry: usize,
    ) -> Result<Response>
    where
        R: Fn(StatusCode) -> RetryCheck + Send + Sync,
    {
        let result = send_retry_reqwest(
            || {
                self.try_clone().ok_or_else(|| {
                    anyhow::Error::msg("This request cannot be retried because it cannot be cloned")
                })
            },
            check_status,
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
    use wiremock::{MockServer, Mock, ResponseTemplate};
    use wiremock::matchers::path;

    async fn build_server() -> Result<MockServer> {
        let server = MockServer::start().await;
        server.register(Mock::given(path("/200")).respond_with(ResponseTemplate::new(200))).await;
        server.register(Mock::given(path("/400")).respond_with(ResponseTemplate::new(400))).await;
        server.register(Mock::given(path("/401")).respond_with(ResponseTemplate::new(401))).await;
        server.register(Mock::given(path("/404")).respond_with(ResponseTemplate::new(404))).await;
        Ok(server)
    }

    fn always_fail(_: StatusCode) -> RetryCheck {
        RetryCheck::Fail
    }

    fn succeed_400(code: StatusCode) -> RetryCheck {
        match code {
            StatusCode::BAD_REQUEST => RetryCheck::Succeed,
            _ => RetryCheck::Retry,
        }
    }

    #[tokio::test]
    async fn retry_success() -> Result<()> {
        let server = build_server().await?;
        reqwest::Client::new()
            .get(format!("{}/200", &server.uri()))
            .send_retry_default()
            .await?
            .error_for_status()?;
        Ok(())
    }

    #[tokio::test]
    async fn retry_socket_failure() -> Result<()> {
        let server = build_server().await?;
        let resp = reqwest::Client::new()
            .get(format!("{}/404", &server.uri()))
            .send_retry(always_retry, Duration::from_millis(1), 3)
            .await;

        match resp {
            Ok(result) => {
                anyhow::bail!("response should have failed: {:?}", result);
            }
            Err(err) => {
                let as_text = format!("{:?}", err);
                assert!(as_text.contains("request attempt 4 failed"), "{}", as_text);
            }
        }

        Ok(())
    }

    #[tokio::test]
    async fn retry_fail_normal() -> Result<()> {
        let server = build_server().await?;
        let resp = reqwest::Client::new()
            .get(format!("{}/400", &server.uri()))
            .send_retry(always_retry, Duration::from_millis(1), 3)
            .await;

        match resp {
            Ok(result) => {
                anyhow::bail!("response should have failed: {:?}", result);
            }
            Err(err) => {
                let as_text = format!("{:?}", err);
                assert!(as_text.contains("request attempt 4 failed"), "{}", as_text);
            }
        }

        Ok(())
    }

    #[tokio::test]
    async fn retry_fail_fast() -> Result<()> {
        let server = build_server().await?;
        let resp = reqwest::Client::new()
            .get(format!("{}/400", &server.uri()))
            .send_retry(always_fail, Duration::from_millis(1), 3)
            .await;

        assert!(resp.is_err(), "{:?}", resp);
        let as_text = format!("{:?}", resp);
        assert!(as_text.contains("request attempt 1 failed"), "{}", as_text);
        Ok(())
    }

    #[tokio::test]
    async fn retry_400_success() -> Result<()> {
        let server = build_server().await?;
        let resp = reqwest::Client::new()
            .get(format!("{}/400", &server.uri()))
            .send_retry(succeed_400, Duration::from_millis(1), 3)
            .await?;

        assert_eq!(resp.status(), StatusCode::BAD_REQUEST);
        Ok(())
    }

    #[tokio::test]
    async fn retry_400_with_retry() -> Result<()> {
        let server = build_server().await?;
        let resp = reqwest::Client::new()
            .get(format!("{}/401", &server.uri()))
            .send_retry(succeed_400, Duration::from_millis(1), 3)
            .await;

        assert!(resp.is_err(), "{:?}", resp);
        let as_text = format!("{:?}", resp);
        assert!(as_text.contains("request attempt 4 failed"), "{}", as_text);
        Ok(())
    }
}
