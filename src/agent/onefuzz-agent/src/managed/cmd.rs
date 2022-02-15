// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::config::{CommonConfig, Config};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;
use std::time::Duration;

// 100 MB.
const MIN_AVAILABLE_BYTES: u64 = 100 * 1_000_000;

const OOM_CHECK_INTERVAL: Duration = Duration::from_secs(5);

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();
    let config_path = value_t!(args, "config", PathBuf)?;
    let setup_dir = value_t!(args, "setup_dir", PathBuf)?;
    let config = Config::from_file(config_path, setup_dir)?;

    init_telemetry(config.common());

    let result = tokio::select! {
        result = config.run() => result,

        // Ignore this task if it returns due to a querying error.
        Ok(oom) = out_of_memory(MIN_AVAILABLE_BYTES) => {
            // Convert the OOM notification to an error, so we can log it below.
            let err = format_err!("out of memory: {} bytes available, {} required", oom.available_bytes, oom.min_bytes);
            Err(err)
        },
    };

    if let Err(err) = &result {
        error!("error running task: {:?}", err);
    }

    onefuzz_telemetry::try_flush_and_close();
    result
}

// Periodically check available system memory.
//
// If available memory drops below the minimum, exit informatively.
//
// Parameterized to enable future configuration by VMSS.
#[cfg(not(target_os = "macos"))]
async fn out_of_memory(min_bytes: u64) -> Result<OutOfMemory> {
    loop {
        match onefuzz::memory::available_bytes() {
            Ok(available_bytes) => {
                if available_bytes < min_bytes {
                    return Ok(OutOfMemory {
                        available_bytes,
                        min_bytes,
                    });
                }
            }
            Err(err) => {
                warn!("error querying system memory usage: {}", err);
                return Err(err);
            }
        }

        tokio::time::sleep(OOM_CHECK_INTERVAL).await;
    }
}

#[cfg(target_os = "macos")]
async fn out_of_memory(_min_bytes: u64) -> Result<OutOfMemory> {
    std::future::pending()
}

struct OutOfMemory {
    available_bytes: u64,
    min_bytes: u64,
}

fn init_telemetry(config: &CommonConfig) {
    onefuzz_telemetry::set_appinsights_clients(
        config.instance_telemetry_key.clone(),
        config.microsoft_telemetry_key.clone(),
    );
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("managed fuzzing")
        .arg(Arg::with_name("config").required(true))
        .arg(Arg::with_name("setup_dir").required(true))
}
