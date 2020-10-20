// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use rand::prelude::*;
use std::time::Duration;

pub fn jitter(value: Duration) -> Duration {
    let random: u64 = thread_rng().gen_range(0, 10);
    Duration::from_secs(random) + value
}
