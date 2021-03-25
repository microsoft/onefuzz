// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::afl;
use anyhow::{Error, Result};
use onefuzz::jitter::delay_with_jitter;
use onefuzz_telemetry::{track_event, Event::runtime_stats};
use serde::Deserialize;
pub const STATS_DELAY: std::time::Duration = std::time::Duration::from_secs(30);

// TODO - remove unkonwn_lints once GitHub build agents are at 1.51.0 or later
#[allow(unknown_lints)]
#[allow(clippy::upper_case_acronyms)]
#[derive(Debug, Deserialize, Clone)]
pub enum StatsFormat {
    AFL,
}

pub async fn monitor_stats(path: Option<String>, format: Option<StatsFormat>) -> Result<(), Error> {
    if let Some(path) = path {
        if let Some(format) = format {
            loop {
                let stats = match format {
                    StatsFormat::AFL => afl::read_stats(&path).await,
                };
                if let Ok(stats) = stats {
                    track_event(runtime_stats, stats);
                }
                delay_with_jitter(STATS_DELAY).await;
            }
        }
    }
    Ok(())
}
