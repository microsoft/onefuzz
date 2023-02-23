// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        SyncCountDirMonitor, UiEvent, CHECK_FUZZER_HELP, CHECK_RETRY_COUNT, CRASHES_DIR,
        DISABLE_CHECK_QUEUE, NO_REPRO_DIR, REPORTS_DIR, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::{
        config::CommonConfig,
        report::libfuzzer_report::{Config, ReportTask},
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use storage_queue::QueueClient;

pub fn build_report_config(
    args: &clap::ArgMatches,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;
    let reports = get_synced_dir(REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let no_repro = get_synced_dir(NO_REPRO_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let unique_reports = get_synced_dir(UNIQUE_REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();

    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has a default");

    let check_queue = !args.get_flag(DISABLE_CHECK_QUEUE);

    let check_fuzzer_help = args.get_flag(CHECK_FUZZER_HELP);

    let crashes = if input_queue.is_none() { crashes } else { None };

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_retry_count,
        check_fuzzer_help,
        minimized_stack_depth: None,
        input_queue,
        check_queue,
        crashes,
        reports,
        no_repro,
        unique_reports,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_report_config(args, None, context.common_config.clone(), event_sender)?;
    ReportTask::new(config).managed_run().await
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(CRASHES_DIR)
            .long(CRASHES_DIR)
            .required(true)
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
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64))
            .long(TARGET_TIMEOUT),
        Arg::new(CHECK_RETRY_COUNT)
            .long(CHECK_RETRY_COUNT)
            .value_parser(value_parser!(u64))
            .default_value("0"),
        Arg::new(DISABLE_CHECK_QUEUE)
            .action(ArgAction::SetTrue)
            .long(DISABLE_CHECK_QUEUE),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only libfuzzer crash report task")
        .args(&build_shared_args())
}
