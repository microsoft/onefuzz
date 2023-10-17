// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
use std::path::PathBuf;

use anyhow::Result;
use clap::{value_parser, Arg, Command};
use ipc_channel::ipc::{self, IpcReceiver, IpcSender};

use flexi_logger::{Duplicate, FileSpec, Logger, WriteMode};
use onefuzz::ipc::IpcMessageKind;
use onefuzz_telemetry::{error, info, warn};
use std::time::Duration;
use tokio::task;

use onefuzz_task_lib::tasks::config::{CommonConfig, Config};

const OOM_CHECK_INTERVAL: Duration = Duration::from_secs(5);

pub async fn run(args: &clap::ArgMatches) -> Result<()> {
    let _logger = Logger::try_with_env_or_str("info")?
        .log_to_file(
            FileSpec::default()
                .directory(".")
                .basename("task_log")
                .use_timestamp(false)
                .suffix("txt"),
        )
        .format_for_files(|w, now, record| {
            write!(
                w,
                "[{}] [{}] {}",
                now.now_utc_owned().format("%Y-%m-%d %H:%M:%S%.6f UTC"),
                record.level(),
                &record.args()
            )
        })
        .duplicate_to_stderr(Duplicate::Warn)
        .write_mode(WriteMode::BufferAndFlush)
        .start()?;

    let config_path = args
        .get_one::<PathBuf>(CONFIG_ARG)
        .expect("marked as required");

    let setup_dir = args
        .get_one::<PathBuf>(SETUP_DIR_ARG)
        .expect("marked as required");

    let extra_setup_dir = args
        .get_one::<PathBuf>(EXTRA_SETUP_DIR_ARG)
        .map(ToOwned::to_owned);

    let config = Config::from_file(config_path, setup_dir.clone(), extra_setup_dir)?;

    info!("Creating channel from agent to task");
    let (agent_sender, receive_from_agent): (
        IpcSender<IpcMessageKind>,
        IpcReceiver<IpcMessageKind>,
    ) = ipc::channel()?;
    info!("Connecting...");
    let oneshot_sender = IpcSender::connect(config.common().from_agent_to_task_endpoint.clone())?;
    info!("Sending sender to agent");
    oneshot_sender.send(agent_sender)?;

    info!("Creating channel from task to agent");
    // For now, the task_sender is unused since the task isn't sending any messages to the agent yet
    // In the future, when we may want to send telemetry through this ipc channel for example, we can use the task_sender
    let (_task_sender, receive_from_task): (
        IpcSender<IpcMessageKind>,
        IpcReceiver<IpcMessageKind>,
    ) = ipc::channel()?;
    info!("Connecting...");
    let oneshot_receiver = IpcSender::connect(config.common().from_task_to_agent_endpoint.clone())?;
    info!("Sending receiver to agent");
    oneshot_receiver.send(receive_from_task)?;

    let shutdown_listener = task::spawn_blocking(move || loop {
        match receive_from_agent.recv() {
            Ok(msg) => info!("Received unexpected message from agent: {:?}", msg),
            Err(ipc::IpcError::Disconnected) => {
                info!("Agent disconnected from the IPC channel. Shutting down");
                break;
            }
            Err(ipc::IpcError::Bincode(e)) => {
                error!("BinCode error receiving message from agent: {:?}", e);
                break;
            }
            Err(ipc::IpcError::Io(e)) => {
                error!("IO error receiving message from agent: {:?}", e);
                break;
            }
        }
    });

    init_telemetry(config.common()).await;

    let min_available_memory_bytes = 1_000_000 * config.common().min_available_memory_mb;

    // If the memory limit is 0, this will never return.
    let check_oom = out_of_memory(min_available_memory_bytes);

    let result = tokio::select! {
        result = config.run() => result,

        // Ignore this task if it returns due to a querying error.
        Ok(oom) = check_oom => {
            // Convert the OOM notification to an error, so we can log it below.
            let err = anyhow::format_err!("out of memory: {} bytes available, {} required", oom.available_bytes, oom.min_bytes);
            Err(err)
        },

        _shutdown = shutdown_listener => {
            Ok(())
        }
    };

    if let Err(err) = &result {
        error!("error running task: {:?}", err);
    }

    onefuzz_telemetry::try_flush_and_close().await;

    result
}

const MAX_OOM_QUERY_ERRORS: usize = 5;

// Periodically check available system memory.
//
// If available memory drops below the minimum, exit informatively.
//
// Parameterized to enable future configuration by VMSS.
async fn out_of_memory(min_bytes: u64) -> Result<OutOfMemory> {
    match min_bytes {
        0 => log::info!("memory watchdog is disabled: this task may fail suddenly if it runs out of memory."),
        _ => log::info!("memory watchdog is enabled: this task will fail informatively if there are {} bytes or fewer of usable memory left.", min_bytes),
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

const CONFIG_ARG: &str = "config";
const SETUP_DIR_ARG: &str = "setup_dir";
const EXTRA_SETUP_DIR_ARG: &str = "extra_setup_dir";

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("managed fuzzing")
        .arg(
            Arg::new(CONFIG_ARG)
                .required(true)
                .value_parser(value_parser!(PathBuf)),
        )
        .arg(
            Arg::new(SETUP_DIR_ARG)
                .required(true)
                .value_parser(value_parser!(PathBuf)),
        )
        .arg(
            Arg::new(EXTRA_SETUP_DIR_ARG)
                .required(false)
                .value_parser(value_parser!(PathBuf)),
        )
}
