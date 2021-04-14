// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        SyncCountDirMonitor, UiEvent, CHECK_ASAN_LOG, CHECK_RETRY_COUNT, CRASHES_DIR,
        DISABLE_CHECK_DEBUGGER, DISABLE_CHECK_QUEUE, NO_REPRO_DIR, REPORTS_DIR, TARGET_ENV,
        TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::{
        config::CommonConfig,
        report::generic::{Config, ReportTask},
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use flume::Sender;
use storage_queue::QueueClient;

pub fn build_report_config(
    args: &clap::ArgMatches<'_>,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let crashes = Some(get_synced_dir(
        CRASHES_DIR,
        common.job_id,
        common.task_id,
        args,
    )?)
    .monitor_count(&event_sender)?;
    let reports = get_synced_dir(REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;
    let no_repro = get_synced_dir(NO_REPRO_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let unique_reports = Some(get_synced_dir(
        UNIQUE_REPORTS_DIR,
        common.job_id,
        common.task_id,
        args,
    )?)
    .monitor_count(&event_sender)?;

    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();

    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;
    let check_queue = !args.is_present(DISABLE_CHECK_QUEUE);
    let check_asan_log = args.is_present(CHECK_ASAN_LOG);
    let check_debugger = !args.is_present(DISABLE_CHECK_DEBUGGER);

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_asan_log,
        check_debugger,
        check_retry_count,
        check_queue,
        crashes,
        minimized_stack_depth: None,
        input_queue,
        no_repro,
        reports,
        unique_reports,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone())?;
    let config = build_report_config(args, None, context.common_config.clone(), event_sender)?;
    ReportTask::new(config).managed_run().await
}

pub fn build_shared_args() -> Vec<Arg<'static, 'static>> {
    vec![
        Arg::with_name(TARGET_EXE)
            .long(TARGET_EXE)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_ENV)
            .long(TARGET_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(TARGET_OPTIONS)
            .default_value("{input}")
            .long(TARGET_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(CRASHES_DIR)
            .long(CRASHES_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(REPORTS_DIR)
            .long(REPORTS_DIR)
            .takes_value(true)
            .required(false),
        Arg::with_name(NO_REPRO_DIR)
            .long(NO_REPRO_DIR)
            .takes_value(true)
            .required(false),
        Arg::with_name(UNIQUE_REPORTS_DIR)
            .long(UNIQUE_REPORTS_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_TIMEOUT)
            .takes_value(true)
            .long(TARGET_TIMEOUT)
            .default_value("30"),
        Arg::with_name(CHECK_RETRY_COUNT)
            .takes_value(true)
            .long(CHECK_RETRY_COUNT)
            .default_value("0"),
        Arg::with_name(DISABLE_CHECK_QUEUE)
            .takes_value(false)
            .long(DISABLE_CHECK_QUEUE),
        Arg::with_name(CHECK_ASAN_LOG)
            .takes_value(false)
            .long(CHECK_ASAN_LOG),
        Arg::with_name(DISABLE_CHECK_DEBUGGER)
            .takes_value(false)
            .long(DISABLE_CHECK_DEBUGGER),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only generic crash report")
        .args(&build_shared_args())
}
