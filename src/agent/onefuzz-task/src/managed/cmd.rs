// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
use std::path::PathBuf;

use anyhow::Result;
use clap::{Arg, Command};
use std::time::Duration;

use crate::tasks::{
    config::{CommonConfig, Config},
    task_logger,
};

const OOM_CHECK_INTERVAL: Duration = Duration::from_secs(5);

pub async fn run(args: &clap::ArgMatches) -> Result<()> {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();

    let config_path = args
        .get_one::<PathBuf>("config")
        .expect("marked as required");

    let setup_dir = args
        .get_one::<PathBuf>("setup_dir")
        .expect("marked as required");

    let extra_dir = args.get_one::<PathBuf>("extra_dir").map(|f| f.as_path());
    let config = Config::from_file(config_path, setup_dir, extra_dir)?;

    init_telemetry(config.common()).await;

    let min_available_memory_bytes = 1_000_000 * config.common().min_available_memory_mb;

    // If the memory limit is 0, this will resolve immediately with an error.
    let check_oom = out_of_memory(min_available_memory_bytes);

    let common = config.common().clone();
    let machine_id = common.machine_identity.machine_id;
    let task_logger = if let Some(logs) = common.logs.clone() {
        let rx = onefuzz_telemetry::subscribe_to_events()?;

        let logger = task_logger::TaskLogger::new(common.job_id, common.task_id, machine_id);

        Some(logger.start(rx, logs).await?)
    } else {
        None
    };

    let result = tokio::select! {
        result = config.run() => result,

        // Ignore this task if it returns due to a querying error.
        Ok(oom) = check_oom => {
            // Convert the OOM notification to an error, so we can log it below.
            let err = format_err!("out of memory: {} bytes available, {} required", oom.available_bytes, oom.min_bytes);
            Err(err)
        },
    };

    if let Err(err) = &result {
        error!("error running task: {:?}", err);
    }

    onefuzz_telemetry::try_flush_and_close().await;

    // wait for the task logger to finish
    if let Some(task_logger) = task_logger {
        let _ = task_logger.flush_and_stop(Duration::from_secs(60)).await;
    }

    result
}

const MAX_OOM_QUERY_ERRORS: usize = 5;

// Periodically check available system memory.
//
// If available memory drops below the minimum, exit informatively.
//
// Parameterized to enable future configuration by VMSS.
async fn out_of_memory(min_bytes: u64) -> Result<OutOfMemory> {
    if min_bytes == 0 {
        bail!("available memory minimum is unreachable");
    }

    let mut consecutive_query_errors = 0;

    loop {
        match onefuzz::memory::available_bytes() {
            Ok(available_bytes) => {
                // Reset so we count consecutive errors.
                consecutive_query_errors = 0;

                if available_bytes < min_bytes {
                    return Ok(OutOfMemory {
                        available_bytes,
                        min_bytes,
                    });
                }
            }
            Err(err) => {
                warn!("error querying system memory usage: {}", err);

                consecutive_query_errors += 1;

                if consecutive_query_errors > MAX_OOM_QUERY_ERRORS {
                    return Err(err);
                }
            }
        }

        tokio::time::sleep(OOM_CHECK_INTERVAL).await;
    }
}

struct OutOfMemory {
    available_bytes: u64,
    min_bytes: u64,
}

async fn init_telemetry(config: &CommonConfig) {
    onefuzz_telemetry::set_appinsights_clients(
        config.instance_telemetry_key.clone(),
        config.microsoft_telemetry_key.clone(),
    )
    .await;
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("managed fuzzing")
        .arg(
            Arg::new("config")
                .required(true)
                .value_parser(value_parser!(PathBuf)),
        )
        .arg(
            Arg::new("setup_dir")
                .required(true)
                .value_parser(value_parser!(PathBuf)),
        )
        .arg(
            Arg::new("extra_dir")
                .required(false)
                .value_parser(value_parser!(PathBuf)),
        )
}
