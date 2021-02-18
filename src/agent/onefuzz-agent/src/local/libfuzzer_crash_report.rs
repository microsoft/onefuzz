// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType, CHECK_FUZZER_HELP,
        CHECK_RETRY_COUNT, CRASHES_DIR, DISABLE_CHECK_QUEUE, NO_REPRO_DIR, REPORTS_DIR, TARGET_ENV,
        TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::report::libfuzzer_report::{Config, ReportTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use futures::stream::StreamExt;
use onefuzz::monitor::DirectoryMonitor;
use reqwest::Url;
use std::path::{Path, PathBuf};
use tokio::task::JoinHandle;

use tempfile::tempdir;

async fn monitor_folder_into_queue(path: impl AsRef<Path>, queue_url: Url) -> Result<()> {
    let queue = storage_queue::QueueClient::new(queue_url.clone())?;

    let mut monitor = DirectoryMonitor::new(PathBuf::from(path.as_ref()));
    monitor.start()?;
    while let Some(crash) = monitor.next().await {
        let file_url = Url::from_file_path(crash).map_err(|_| anyhow!("invalid file path"))?;
        queue.enqueue(file_url).await?
    }
    Ok(())
}

pub fn build_report_config(
    args: &clap::ArgMatches<'_>,
) -> Result<(Config, JoinHandle<Result<()>>)> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let crashes = value_t!(args, CRASHES_DIR, PathBuf)?;
    let reports = if args.is_present(REPORTS_DIR) {
        Some(value_t!(args, REPORTS_DIR, PathBuf)?).map(|x| x.into())
    } else {
        None
    };
    let no_repro = if args.is_present(NO_REPRO_DIR) {
        Some(value_t!(args, NO_REPRO_DIR, PathBuf)?).map(|x| x.into())
    } else {
        None
    };
    let unique_reports = Some(value_t!(args, UNIQUE_REPORTS_DIR, PathBuf)?.into());

    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();

    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;

    let check_queue = !args.is_present(DISABLE_CHECK_QUEUE);

    let check_fuzzer_help = args.is_present(CHECK_FUZZER_HELP);

    let queue_file = tempdir()?;

    let input_queue =
        Url::from_file_path(queue_file.path()).map_err(|_| anyhow!("invalid file path"))?;

    let file_monitor = tokio::spawn(monitor_folder_into_queue(
        crashes.clone(),
        input_queue.clone(),
    ));

    let common = build_common_config(args)?;
    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_retry_count,
        check_fuzzer_help,
        input_queue: Some(input_queue),
        check_queue,
        crashes: Some(crashes.into()),
        reports,
        no_repro,
        unique_reports,
        common,
    };
    Ok((config, file_monitor))
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let (config, file_monitor) = build_report_config(args)?;
    let _run_handle = tokio::task::spawn(file_monitor);
    ReportTask::new(config).managed_run().await

    // let run = tokio::try_join!(run_handle, file_monitor)?;
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
            .long(TARGET_TIMEOUT),
        Arg::with_name(CHECK_RETRY_COUNT)
            .takes_value(true)
            .long(CHECK_RETRY_COUNT)
            .default_value("0"),
        Arg::with_name(DISABLE_CHECK_QUEUE)
            .takes_value(false)
            .long(DISABLE_CHECK_QUEUE),
        Arg::with_name(CHECK_FUZZER_HELP)
            .takes_value(false)
            .long(CHECK_FUZZER_HELP),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer crash report task")
        .args(&build_shared_args())
}
