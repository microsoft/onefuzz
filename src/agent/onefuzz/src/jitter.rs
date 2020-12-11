// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use rand::prelude::*;
use std::time::Duration;
use tokio::time::delay_for;

pub fn jitter(value: Duration) -> Duration {
    let random: u64 = thread_rng().gen_range(0, 10);
    Duration::from_secs(random) + value
}

pub async fn delay_with_jitter(value: Duration) {
    delay_for(jitter(value)).await
}

pub async fn delay_with_jitter_size(value: Duration) {
    let random: u64 = thread_rng().gen_range(0, value.as_secs());
    let delay = Duration::new(random, 0);
    delay_for(delay).await
}
