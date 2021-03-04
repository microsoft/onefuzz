// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::jitter::delay_with_jitter;
use anyhow::Result;
use async_trait::async_trait;
use std::time::Duration;

#[async_trait]
pub trait CheckNotify {
    async fn is_notified(&self, delay: Duration) -> bool;
}

#[async_trait]
impl CheckNotify for tokio::sync::Notify {
    async fn is_notified(&self, delay: Duration) -> bool {
        let notify = self;
        tokio::select! {
            () = delay_with_jitter(delay) => false,
            () = notify.notified() => true,
        }
    }
}

/// wait on all join handles until they all return a success value or
/// the first failure.
pub async fn try_wait_all_join_handles(
    handles: Vec<tokio::task::JoinHandle<Result<()>>>,
) -> Result<()> {
    let mut tasks = handles;
    loop {
        let (result, _, remaining_tasks) = futures::future::select_all(tasks).await;
        result??;

        if remaining_tasks.is_empty() {
            return Ok(());
        } else {
            tasks = remaining_tasks
        }
    }
}
