// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(any(target_os = "linux", target_os = "windows"))]
use crate::{
    local::{common::COVERAGE_DIR, coverage, coverage::build_shared_args as build_coverage_args},
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
        analysis::generic::run as run_analysis,
        config::CommonConfig,
        fuzz::libfuzzer::{common::default_workers, generic::LibFuzzerFuzzTask},
        regression::libfuzzer::LibFuzzerRegressionTask,
        report::libfuzzer_report::ReportTask,
        utils::default_bool_true,
    },
};
use anyhow::Result;
use async_trait::async_trait;
use clap::Command;
use flume::Sender;
use onefuzz::{syncdir::SyncedDir, utils::try_wait_all_join_handles};
use schemars::JsonSchema;
use std::{
    collections::{HashMap, HashSet},
    path::PathBuf,
};
use tokio::task::spawn;
use uuid::Uuid;

use super::template::{RunContext, Template};

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
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

    if args.contains_id(UNIQUE_REPORTS_DIR) {
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

    #[cfg(any(target_os = "linux", target_os = "windows"))]
    if args.contains_id(COVERAGE_DIR) {
        let coverage_input_monitor =
            DirectoryMonitorQueue::start_monitoring(crash_dir.clone()).await?;
        let coverage_config = coverage::build_coverage_config(
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

    if args.contains_id(ANALYZER_EXE) {
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

    if args.contains_id(REGRESSION_REPORTS_DIR) {
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

pub fn args(name: &'static str) -> Command {
    let mut app = Command::new(name).about("run a local libfuzzer & crash reporting task");

    let mut used = HashSet::new();

    for args in &[
        build_fuzz_args(),
        build_crash_args(),
        build_analysis_args(false),
        #[cfg(any(target_os = "linux", target_os = "windows"))]
        build_coverage_args(true),
        build_regression_args(false),
    ] {
        for arg in args {
            if used.insert(arg.get_id()) {
                app = app.arg(arg);
            }
        }
    }

    app
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibFuzzer {
    inputs: PathBuf,
    readonly_inputs: Vec<PathBuf>,
    crashes: PathBuf,
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_workers: Option<usize>,
    ensemble_sync_delay: Option<u64>,
    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    expect_crash_on_failure: bool,
}

#[async_trait]
impl Template for LibFuzzer {
    async fn run(&self, context: &RunContext) -> Result<()> {
        let ri: Result<Vec<SyncedDir>> = self
            .readonly_inputs
            .iter()
            .enumerate()
            .map(|(index, input)| context.to_sync_dir(format!("readonly_inputs_{index}"), input))
            .collect();

        let libfuzzer_config = crate::tasks::fuzz::libfuzzer::generic::Config {
            inputs: context.to_monitored_sync_dir("inputs", &self.inputs)?,
            readonly_inputs: Some(ri?),
            crashes: context.to_monitored_sync_dir("crashes", &self.crashes)?,
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_workers: self.target_workers.unwrap_or(default_workers()),
            ensemble_sync_delay: self.ensemble_sync_delay,
            check_fuzzer_help: self.check_fuzzer_help,
            expect_crash_on_failure: self.expect_crash_on_failure,
            extra: (),
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let fuzzer = LibFuzzerFuzzTask::new(libfuzzer_config)?;
                fuzzer.run().await
            })
            .await;
        Ok(())
    }
}
