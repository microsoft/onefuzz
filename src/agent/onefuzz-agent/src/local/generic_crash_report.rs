// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_target_cmd_options, build_common_config, get_target_env, CHECK_RETRY_COUNT,
        CRASHES_DIR, DISABLE_CHECK_QUEUE, NO_REPRO_DIR, REPORTS_DIR, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::report::generic::{Config, ReportTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use std::path::PathBuf;

pub async fn build_report_config(args: &clap::ArgMatches<'_>) -> Result<Config> {
    let target_exe = value_t!(args, TARGET_EXE, PathBuf)?;
    let crashes = Some(value_t!(args, CRASHES_DIR, PathBuf)?.into());
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
    let unique_reports = value_t!(args, UNIQUE_REPORTS_DIR, PathBuf)?.into();

    let target_options = args.values_of_lossy(TARGET_OPTIONS).unwrap_or_default();
    let target_env = get_target_env(args)?;

    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();

    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;
    let check_queue = !args.is_present(DISABLE_CHECK_QUEUE);
    let check_asan_log = args.is_present("check_asan_log");
    let check_debugger = !args.is_present("disable_check_debugger");

    let common = build_common_config(args)?;

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
        input_queue: None,
        no_repro,
        reports,
        unique_reports,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let config = build_report_config(args).await?;
    ReportTask::new(&config).local_run().await
}

pub fn add_report_options(app: App<'static, 'static>) -> App<'static, 'static> {
    app.arg(Arg::with_name(CRASHES_DIR).takes_value(true).required(true))
        .arg(
            Arg::with_name(REPORTS_DIR)
                .long(REPORTS_DIR)
                .takes_value(true)
                .required(false),
        )
        .arg(
            Arg::with_name(NO_REPRO_DIR)
                .long(NO_REPRO_DIR)
                .takes_value(true)
                .required(false),
        )
        .arg(
            Arg::with_name(UNIQUE_REPORTS_DIR)
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name(TARGET_TIMEOUT)
                .takes_value(true)
                .long(TARGET_TIMEOUT)
                .default_value("5"),
        )
        .arg(
            Arg::with_name(CHECK_RETRY_COUNT)
                .takes_value(true)
                .long(CHECK_RETRY_COUNT)
                .default_value("0"),
        )
        .arg(
            Arg::with_name(DISABLE_CHECK_QUEUE)
                .takes_value(false)
                .long(DISABLE_CHECK_QUEUE),
        )
        .arg(
            Arg::with_name("check_asan_log")
                .takes_value(false)
                .long("check_asan_log"),
        )
        .arg(
            Arg::with_name("disable_check_debugger")
                .takes_value(false)
                .long("disable_check_debugger"),
        )
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("execute a local-only generic crash report");
    app = add_target_cmd_options(true, true, true, app);
    add_report_options(app)
}
