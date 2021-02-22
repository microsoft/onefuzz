// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::{DirectoryMonitorQueue, COVERAGE_DIR},
        libfuzzer_coverage::{build_coverage_config, build_shared_args as build_coverage_args},
        libfuzzer_crash_report::{build_report_config, build_shared_args as build_crash_args},
        libfuzzer_fuzz::{build_fuzz_config, build_shared_args as build_fuzz_args},
    },
    tasks::{
        coverage::libfuzzer_coverage::CoverageTask, fuzz::libfuzzer_fuzz::LibFuzzerFuzzTask,
        report::libfuzzer_report::ReportTask,
    },
};
use anyhow::Result;
use clap::{App, SubCommand};
use std::collections::HashSet;
use tokio::task::spawn;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let fuzz_config = build_fuzz_config(args)?;
    let crash_dir = fuzz_config.crashes.path.clone();
    let crash_dir_monitor = DirectoryMonitorQueue::start_monitoring(crash_dir.clone()).await?;
    let fuzzer = LibFuzzerFuzzTask::new(fuzz_config)?;
    fuzzer.check_libfuzzer().await?;
    let fuzz_task = spawn(async move { fuzzer.managed_run().await });

    let report_config = build_report_config(args, Some(crash_dir_monitor.queue_url))?;
    let mut report = ReportTask::new(report_config);
    let report_task = spawn(async move { report.managed_run().await });

    if args.is_present(COVERAGE_DIR) {
        let crash_dir_monitor = DirectoryMonitorQueue::start_monitoring(crash_dir).await?;
        let coverage_config = build_coverage_config(args, true, Some(crash_dir_monitor.queue_url))?;
        let mut coverage = CoverageTask::new(coverage_config);
        let coverage_task = spawn(async move { coverage.managed_run().await });

        let result = tokio::try_join!(
            fuzz_task,
            report_task,
            coverage_task,
            crash_dir_monitor.handle
        )?;
        result.0?;
        result.1?;
        result.2?;
    } else {
        let result = tokio::try_join!(fuzz_task, report_task, crash_dir_monitor.handle)?;
        result.0?;
        result.1?;
    }

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local libfuzzer & crash reporting task");

    let mut used = HashSet::new();
    for args in &[
        build_fuzz_args(),
        build_crash_args(),
        build_coverage_args(true),
    ] {
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
