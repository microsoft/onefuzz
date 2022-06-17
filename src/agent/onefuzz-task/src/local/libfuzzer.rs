// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(any(target_os = "linux", target_os = "windows"))]
use crate::{
    local::{
        common::COVERAGE_DIR,
        coverage::{build_coverage_config, build_shared_args as build_coverage_args},
    },
    tasks::coverage::generic::CoverageTask,
};
use crate::{
    local::{
        common::{
            build_local_context, wait_for_dir, DirectoryMonitorQueue, UiEvent, ANALYZER_EXE,
            REGRESSION_REPORTS_DIR, UNIQUE_REPORTS_DIR,
        },
        generic_analysis::{build_analysis_config, build_shared_args as build_analysis_args},
        libfuzzer_crash_report::{build_report_config, build_shared_args as build_crash_args},
        libfuzzer_fuzz::{build_fuzz_config, build_shared_args as build_fuzz_args},
        libfuzzer_regression::{
            build_regression_config, build_shared_args as build_regression_args,
        },
    },
    tasks::{
        analysis::generic::run as run_analysis, config::CommonConfig,
        fuzz::libfuzzer_fuzz::LibFuzzerFuzzTask, regression::libfuzzer::LibFuzzerRegressionTask,
        report::libfuzzer_report::ReportTask,
    },
};
use anyhow::Result;
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
        .expect("invalid crash dir remote location");

    let fuzzer = LibFuzzerFuzzTask::new(fuzz_config)?;
    let mut task_handles = vec![];

    let fuzz_task = spawn(async move { fuzzer.run().await });

    wait_for_dir(&crash_dir).await?;

    task_handles.push(fuzz_task);

    if args.is_present(UNIQUE_REPORTS_DIR) {
        let crash_report_input_monitor =
            DirectoryMonitorQueue::start_monitoring(crash_dir.clone()).await?;

        let report_config = build_report_config(
            args,
            Some(crash_report_input_monitor.queue_client),
            CommonConfig {
                task_id: Uuid::new_v4(),
                ..context.common_config.clone()
            },
            event_sender.clone(),
        )?;

        let mut report = ReportTask::new(report_config);
        let report_task = spawn(async move { report.managed_run().await });

        task_handles.push(report_task);
        task_handles.push(crash_report_input_monitor.handle);
    }

    // TODO: Maybe branch off here and check for dotnet coverage?

    #[cfg(any(target_os = "linux", target_os = "windows"))]
    if args.is_present(COVERAGE_DIR) {
        let coverage_input_monitor =
            DirectoryMonitorQueue::start_monitoring(crash_dir.clone()).await?;
        let coverage_config = build_coverage_config(
            args,
            true,
            Some(coverage_input_monitor.queue_client),
            CommonConfig {
                task_id: Uuid::new_v4(),
                ..context.common_config.clone()
            },
            event_sender.clone(),
        )?;

        let mut coverage = CoverageTask::new(coverage_config);
        let coverage_task = spawn(async move { coverage.run().await });

        task_handles.push(coverage_task);
        task_handles.push(coverage_input_monitor.handle);
    }

    if args.is_present(ANALYZER_EXE) {
        let analysis_input_monitor = DirectoryMonitorQueue::start_monitoring(crash_dir).await?;
        let analysis_config = build_analysis_config(
            args,
            Some(analysis_input_monitor.queue_client),
            CommonConfig {
                task_id: Uuid::new_v4(),
                ..context.common_config.clone()
            },
            event_sender.clone(),
        )?;
        let analysis_task = spawn(async move { run_analysis(analysis_config).await });

        task_handles.push(analysis_task);
        task_handles.push(analysis_input_monitor.handle);
    }

    if args.is_present(REGRESSION_REPORTS_DIR) {
        let regression_config = build_regression_config(
            args,
            CommonConfig {
                task_id: Uuid::new_v4(),
                ..context.common_config.clone()
            },
            event_sender,
        )?;
        let regression = LibFuzzerRegressionTask::new(regression_config);
        let regression_task = spawn(async move { regression.run().await });
        task_handles.push(regression_task);
    }

    try_wait_all_join_handles(task_handles).await?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local libfuzzer & crash reporting task");

    let mut used = HashSet::new();

    for args in [
        build_fuzz_args(),
        build_crash_args(),
        build_analysis_args(false),
        #[cfg(any(target_os = "linux", target_os = "windows"))]
        build_coverage_args(true),
        build_regression_args(false),
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
