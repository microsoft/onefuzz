// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::afl;
use anyhow::{Error, Result};
use onefuzz::jitter::delay_with_jitter;
use onefuzz_telemetry::Event::runtime_stats;
use serde::Deserialize;
pub const STATS_DELAY: std::time::Duration = std::time::Duration::from_secs(30);

// TODO - remove unkonwn_lints once GitHub build agents are at 1.51.0 or later
#[derive(Debug, Deserialize, Clone)]
pub enum StatsFormat {
    #[serde(alias = "AFL")]
    Afl,
}

pub async fn monitor_stats(path: Option<String>, format: Option<StatsFormat>) -> Result<(), Error> {
    if let Some(path) = path {
        if let Some(format) = format {
            loop {
                let stats = match format {
                    StatsFormat::Afl => afl::read_stats(&path).await,
                };
                if let Ok(stats) = stats {
                    log_events!(runtime_stats; stats);
                }
                delay_with_jitter(STATS_DELAY).await;
            }
        }
    }
    Ok(())
}
