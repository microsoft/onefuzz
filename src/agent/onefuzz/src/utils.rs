// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::jitter::delay_with_jitter;
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
