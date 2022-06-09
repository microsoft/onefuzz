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

#[cfg(test)]
mod test {
    use super::*;
    use anyhow::Result;
    use futures::*;
    use std::sync::Arc;
    use tokio::{spawn, sync::Notify, task::JoinHandle, time::sleep};

    fn spawn_ok() -> (Arc<Notify>, JoinHandle<Result<()>>) {
        let notify = Arc::new(Notify::new());

        let notify_clone = notify.clone();
        let handle = spawn(async move {
            notify_clone.notified().await;
            Ok(())
        });
        (notify, handle)
    }

    fn spawn_err() -> (Arc<Notify>, JoinHandle<Result<()>>) {
        let notify = Arc::new(Notify::new());

        let notify_clone = notify.clone();
        let handle = spawn(async move {
            notify_clone.notified().await;
            bail!("error")
        });
        (notify, handle)
    }

    #[tokio::test]
    async fn test_pending_when_no_return() {
        let (_notify1, handle1) = spawn_ok();
        let (_notify2, handle2) = spawn_ok();
        let (_notify3, handle3) = spawn_ok();

        let try_wait_handle = try_wait_all_join_handles(vec![handle1, handle2, handle3]);
        sleep(Duration::from_secs(1)).await;
        assert!(
            try_wait_handle.now_or_never().is_none(),
            "expected no result"
        );
    }

    #[tokio::test]
    async fn test_pending_when_some_return() {
        let (notify1, handle1) = spawn_ok();
        let (notify2, handle2) = spawn_ok();
        let (_notify3, handle3) = spawn_ok();

        let try_wait_handle = try_wait_all_join_handles(vec![handle1, handle2, handle3]);

        notify1.notify_one();
        notify2.notify_one();
        sleep(Duration::from_secs(1)).await;
        assert!(
            try_wait_handle.now_or_never().is_none(),
            "expected no result"
        );
    }

    #[tokio::test]
    async fn test_ready_when_all_return() {
        let (notify1, handle1) = spawn_ok();
        let (notify2, handle2) = spawn_ok();
        let (notify3, handle3) = spawn_ok();

        let try_wait_handle = try_wait_all_join_handles(vec![handle1, handle2, handle3]);

        notify1.notify_one();
        notify2.notify_one();
        notify3.notify_one();
        sleep(Duration::from_secs(1)).await;
        if let Some(result) = try_wait_handle.now_or_never() {
            assert!(result.is_ok(), "expected Ok")
        } else {
            panic!("expected result")
        }
    }

    #[tokio::test]
    async fn test_pending_on_no_failure() {
        let (notify1, handle1) = spawn_ok();
        let (_notify2, handle2) = spawn_err();
        let (_notify3, handle3) = spawn_ok();

        let try_wait_handle = try_wait_all_join_handles(vec![handle1, handle2, handle3]);

        notify1.notify_one();
        sleep(Duration::from_secs(1)).await;
        assert!(
            try_wait_handle.now_or_never().is_none(),
            "expected no result"
        );
    }

    #[tokio::test]
    async fn test_pending_on_first_failure() {
        let (_notify1, handle1) = spawn_ok();
        let (notify2, handle2) = spawn_err();
        let (_notify3, handle3) = spawn_ok();

        let try_wait_handle = try_wait_all_join_handles(vec![handle1, handle2, handle3]);

        notify2.notify_one();

        sleep(Duration::from_secs(1)).await;
        if let Some(result) = try_wait_handle.now_or_never() {
            assert!(result.is_err(), "expected error")
        } else {
            panic!("expected result")
        }
    }
}
