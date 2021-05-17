// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Error, Result};
use onefuzz_telemetry::EventData;
use std::path::Path;
use tokio::io::AsyncBufReadExt;

pub async fn read_stats(output_path: impl AsRef<Path>) -> Result<Vec<EventData>, Error> {
    let output_path = output_path.as_ref();
    let f = tokio::fs::File::open(&output_path).await.with_context(|| {
        format!(
            "unable to open AFL stats for read: {}",
            output_path.display()
        )
    })?;
    let mut stats = Vec::new();

    let reader = tokio::io::BufReader::new(f);
    let mut lines = reader.lines();
    while let Ok(Some(line)) = lines.next_line().await {
        let mut name_value = line.split(':');
        let name = name_value.next().unwrap().trim();
        let value = name_value.next().unwrap().trim();

        match name {
            "target_mode" => {
                stats.push(EventData::Mode(value.to_string()));
            }
            "paths_total" => {
                if let Ok(value) = value.parse::<u64>() {
                    stats.push(EventData::CoveragePaths(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "fuzzer_pid" => {
                if let Ok(value) = value.parse::<u32>() {
                    stats.push(EventData::Pid(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "execs_done" => {
                if let Ok(value) = value.parse::<u64>() {
                    stats.push(EventData::Count(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "paths_favored" => {
                if let Ok(value) = value.parse::<u64>() {
                    stats.push(EventData::CoveragePathsFavored(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "paths_found" => {
                if let Ok(value) = value.parse::<u64>() {
                    stats.push(EventData::CoveragePathsFound(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "paths_imported" => {
                if let Ok(value) = value.parse::<u64>() {
                    stats.push(EventData::CoveragePathsImported(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "execs_per_sec" => {
                if let Ok(value) = value.parse::<f64>() {
                    stats.push(EventData::ExecsSecond(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "bitmap_cvg" => {
                let value = value.replace("%", "");
                if let Ok(value) = value.parse::<f64>() {
                    stats.push(EventData::Coverage(value));
                } else {
                    error!("unable to parse telemetry: {:?} {:?}", name, value);
                }
            }
            "command_line" => {
                stats.push(EventData::CommandLine(value.to_string()));
            }
            // ignored telemetry
            "cycles_done" | "afl_banner" | "afl_version" | "start_time" | "last_update"
            | "stability" | "unique_crashes" | "unique_hangs" | "pending_favs"
            | "pending_total" | "variable_paths" | "last_path" | "last_crash" | "last_hang"
            | "execs_since_crash" | "max_depth" | "cur_path" | "exec_timeout" => {}
            _ => {
                warn!("unsupported telemetry: {} {}", name, value);
            }
        }
    }
    Ok(stats)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_stats_parse() {
        let results = read_stats("data/afl-fuzzer_stats.txt").await.unwrap();
        assert!(results.len() > 5);
        assert!(results.contains(&EventData::Pid(26515)));
        assert!(results.contains(&EventData::ExecsSecond(2666.67)));
        assert!(results.contains(&EventData::Mode("default".to_string())));
    }
}
