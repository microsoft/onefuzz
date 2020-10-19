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
            () = tokio::time::delay_for(delay) => false,
            () = notify.notified() => true,
        }
    }
}
