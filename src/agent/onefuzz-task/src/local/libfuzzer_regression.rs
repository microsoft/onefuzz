// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

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
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;

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
