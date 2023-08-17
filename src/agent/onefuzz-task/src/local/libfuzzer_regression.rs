// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        SyncCountDirMonitor, UiEvent, CHECK_FUZZER_HELP, CHECK_RETRY_COUNT, COVERAGE_DIR,
        CRASHES_DIR, NO_REPRO_DIR, REGRESSION_REPORTS_DIR, REPORTS_DIR, TARGET_ENV, TARGET_EXE,
        TARGET_OPTIONS, TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::{
        config::CommonConfig,
        regression::libfuzzer::{Config, LibFuzzerRegressionTask},
        utils::default_bool_true,
    },
};
use anyhow::Result;
use async_trait::async_trait;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use schemars::JsonSchema;

use super::template::{RunContext, Template};

const REPORT_NAMES: &str = "report_names";

pub fn build_regression_config(
    args: &clap::ArgMatches,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;
    let regression_reports =
        get_synced_dir(REGRESSION_REPORTS_DIR, common.job_id, common.task_id, args)?
            .monitor_count(&event_sender)?;
    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has a default value");

    let reports = get_synced_dir(REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;
    let no_repro = get_synced_dir(NO_REPRO_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;
    let unique_reports = get_synced_dir(UNIQUE_REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let report_list: Option<Vec<String>> = args
        .get_many::<String>(REPORT_NAMES)
        .map(|x| x.cloned().collect());

    let check_fuzzer_help = args.get_flag(CHECK_FUZZER_HELP);

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_fuzzer_help,
        check_retry_count,
        crashes,
        regression_reports,
        reports,
        no_repro,
        unique_reports,
        readonly_inputs: None,
        report_list,
        minimized_stack_depth: None,
        common,
    };
    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_regression_config(args, context.common_config.clone(), event_sender)?;
    LibFuzzerRegressionTask::new(config).run().await
}

pub fn build_shared_args(local_job: bool) -> Vec<Arg> {
    let mut args = vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(COVERAGE_DIR)
            .required(!local_job)
            .long(COVERAGE_DIR)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
        Arg::new(TARGET_TIMEOUT)
            .long(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64)),
        Arg::new(CRASHES_DIR)
            .long(CRASHES_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(REGRESSION_REPORTS_DIR)
            .long(REGRESSION_REPORTS_DIR)
            .required(local_job)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(REPORTS_DIR)
            .long(REPORTS_DIR)
            .required(false)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(NO_REPRO_DIR)
            .long(NO_REPRO_DIR)
            .required(false)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(UNIQUE_REPORTS_DIR)
            .long(UNIQUE_REPORTS_DIR)
            .value_parser(value_parser!(PathBuf))
            .required(true),
        Arg::new(CHECK_RETRY_COUNT)
            .long(CHECK_RETRY_COUNT)
            .value_parser(value_parser!(u64))
            .default_value("0"),
    ];
    if local_job {
        args.push(Arg::new(REPORT_NAMES).long(REPORT_NAMES).num_args(0..))
    }
    args
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only libfuzzer regression task")
        .args(&build_shared_args(true))
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerRegression {
    target_exe: PathBuf,

    #[serde(default)]
    target_options: Vec<String>,

    #[serde(default)]
    target_env: HashMap<String, String>,

    target_timeout: Option<u64>,

    crashes: PathBuf,
    regression_reports: PathBuf,
    report_list: Option<Vec<String>>,
    unique_reports: Option<PathBuf>,
    reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,
    readonly_inputs: Option<PathBuf>,

    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    check_retry_count: u64,

    #[serde(default)]
    minimized_stack_depth: Option<usize>,
}

#[async_trait]
impl Template for LibfuzzerRegression {
    async fn run(&self, context: &RunContext) -> Result<()> {
        let libfuzzer_regression = crate::tasks::regression::libfuzzer::Config {
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_timeout: self.target_timeout,
            crashes: context.to_monitored_sync_dir("crashes", self.crashes.clone())?,
            regression_reports: context
                .to_monitored_sync_dir("regression_reports", self.regression_reports.clone())?,
            report_list: self.report_list.clone(),

            unique_reports: self
                .unique_reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("unique_reports", c))
                .transpose()?,
            reports: self
                .reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("reports", c))
                .transpose()?,
            no_repro: self
                .no_repro
                .clone()
                .map(|c| context.to_monitored_sync_dir("no_repro", c))
                .transpose()?,
            readonly_inputs: self
                .readonly_inputs
                .clone()
                .map(|c| context.to_monitored_sync_dir("readonly_inputs", c))
                .transpose()?,

            check_fuzzer_help: self.check_fuzzer_help,
            check_retry_count: self.check_retry_count,
            minimized_stack_depth: self.minimized_stack_depth,

            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };
        context
            .spawn(async move {
                let regression = crate::tasks::regression::libfuzzer::LibFuzzerRegressionTask::new(
                    libfuzzer_regression,
                );
                regression.run().await
            })
            .await;
        Ok(())
    }
}
