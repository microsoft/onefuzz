// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::DirectoryMonitorQueue,
        generic_crash_report::{build_report_config, build_shared_args as build_crash_args},
        generic_generator::{build_fuzz_config, build_shared_args as build_fuzz_args},
    },
    tasks::{fuzz::generator::GeneratorTask, report::generic::ReportTask},
};
use anyhow::Result;
use clap::{App, SubCommand};
use std::collections::HashSet;
use tokio::task::spawn;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let fuzz_config = build_fuzz_config(args)?;
    let crash_dir = fuzz_config.crashes.path.clone();

    let fuzzer = GeneratorTask::new(fuzz_config);
    let fuzz_task = spawn(async move { fuzzer.run().await });

    let crash_report_input_monitor =
        DirectoryMonitorQueue::start_monitoring(crash_dir.clone()).await?;
    let report_config = build_report_config(args, Some(crash_report_input_monitor.queue_url))?;
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
