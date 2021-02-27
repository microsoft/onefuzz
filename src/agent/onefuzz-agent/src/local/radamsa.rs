// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::{build_common_config, DirectoryMonitorQueue},
        generic_crash_report::{build_report_config, build_shared_args as build_crash_args},
        generic_generator::{build_fuzz_config, build_shared_args as build_fuzz_args},
    },
    tasks::{config::CommonConfig, fuzz::generator::GeneratorTask, report::generic::ReportTask},
};
use anyhow::Result;
use clap::{App, SubCommand};
use std::collections::HashSet;
use tokio::task::spawn;
use uuid::Uuid;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let common = build_common_config(args)?;
    let fuzz_config = build_fuzz_config(args, common.clone())?;
    let crash_dir = fuzz_config
        .crashes
        .url
        .as_file_path()
        .expect("invalid crash dir remote location");

    let fuzzer = GeneratorTask::new(fuzz_config);
    let fuzz_task = spawn(async move { fuzzer.run().await });

    let crash_report_input_monitor =
        DirectoryMonitorQueue::start_monitoring(crash_dir, common.job_id).await?;
    let report_config = build_report_config(
        args,
        Some(crash_report_input_monitor.queue_client),
        CommonConfig {
            task_id: Uuid::new_v4(),
            ..common
        },
    )?;
    let report_task = spawn(async move { ReportTask::new(report_config).managed_run().await });

    let result = tokio::try_join!(fuzz_task, report_task, crash_report_input_monitor.handle)?;
    result.0?;
    result.1?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local generator & crash reporting job");

    let mut used = HashSet::new();
    for args in &[build_fuzz_args(), build_crash_args()] {
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
