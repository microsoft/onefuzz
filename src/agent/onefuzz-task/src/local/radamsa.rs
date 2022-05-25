// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::{build_local_context, DirectoryMonitorQueue, UiEvent},
        generic_crash_report::{build_report_config, build_shared_args as build_crash_args},
        generic_generator::{build_fuzz_config, build_shared_args as build_fuzz_args},
    },
    tasks::{config::CommonConfig, fuzz::generator::GeneratorTask, report::generic::ReportTask},
};
use anyhow::{Context, Result};
use clap::{App, SubCommand};
use flume::Sender;
use onefuzz::utils::try_wait_all_join_handles;
use std::collections::HashSet;
use tokio::task::spawn;
use uuid::Uuid;

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone())?;
    let fuzz_config = build_fuzz_config(args, context.common_config.clone(), event_sender.clone())?;
    let crash_dir = fuzz_config
        .crashes
        .remote_url()?
        .as_file_path()
        .ok_or_else(|| format_err!("invalid crash directory"))?;

    tokio::fs::create_dir_all(&crash_dir)
        .await
        .with_context(|| {
            format!(
                "unable to create crashes directory: {}",
                crash_dir.display()
            )
        })?;

    let fuzzer = GeneratorTask::new(fuzz_config);
    let fuzz_task = spawn(async move { fuzzer.run().await });

    let crash_report_input_monitor = DirectoryMonitorQueue::start_monitoring(crash_dir)
        .await
        .context("directory monitor failed")?;
    let report_config = build_report_config(
        args,
        Some(crash_report_input_monitor.queue_client),
        CommonConfig {
            task_id: Uuid::new_v4(),
            ..context.common_config.clone()
        },
        event_sender,
    )?;
    let report_task = spawn(async move { ReportTask::new(report_config).managed_run().await });

    try_wait_all_join_handles(vec![
        fuzz_task,
        report_task,
        crash_report_input_monitor.handle,
    ])
    .await?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local generator & crash reporting job");

    let mut used = HashSet::new();
    for args in [build_fuzz_args(), build_crash_args()] {
        for arg in args {
            if used.contains(arg.b.name) {
                continue;
            }
            used.insert(arg.b.name.to_string());
            app = app.arg(arg);
        }
    }

    app
}
