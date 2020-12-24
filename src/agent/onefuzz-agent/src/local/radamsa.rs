// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::{
            add_cmd_options, CmdType, CHECK_ASAN_LOG, CHECK_RETRY_COUNT, CRASHES_DIR,
            DISABLE_CHECK_QUEUE, INPUTS_DIR, NO_REPRO_DIR, READONLY_INPUTS, RENAME_OUTPUT,
            REPORTS_DIR, TARGET_TIMEOUT, TARGET_WORKERS, TOOLS_DIR, UNIQUE_REPORTS_DIR,
        },
        generic_crash_report::build_report_config,
        generic_generator::build_fuzz_config,
    },
    tasks::{fuzz::generator::GeneratorTask, report::generic::ReportTask},
};

use anyhow::Result;
use clap::{App, Arg, SubCommand};

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let fuzz_config = build_fuzz_config(args)?;
    let report_config = build_report_config(args)?;

    let fuzzer = GeneratorTask::new(fuzz_config);
    let fuzz_task = fuzzer.local_run();

    let report = ReportTask::new(&report_config);
    let report_task = report.local_run();

    tokio::try_join!(fuzz_task, report_task)?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local libfuzzer & crash reporting task");

    app = add_cmd_options(CmdType::Generator, true, true, true, app);
    app = add_cmd_options(CmdType::Target, true, true, true, app);
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
        .arg(Arg::with_name(TOOLS_DIR).takes_value(true).long(TOOLS_DIR))
        .arg(
            Arg::with_name(CHECK_RETRY_COUNT)
                .takes_value(true)
                .long(CHECK_RETRY_COUNT)
                .default_value("0"),
        )
        .arg(
            Arg::with_name(CHECK_ASAN_LOG)
                .takes_value(false)
                .long(CHECK_ASAN_LOG),
        )
        .arg(
            Arg::with_name(RENAME_OUTPUT)
                .takes_value(false)
                .long(RENAME_OUTPUT),
        )
        .arg(
            Arg::with_name(TARGET_TIMEOUT)
                .takes_value(true)
                .long(TARGET_TIMEOUT)
                .default_value("30"),
        )
        .arg(
            Arg::with_name("disable_check_debugger")
                .takes_value(false)
                .long("disable_check_debugger"),
        )
        .arg(
            Arg::with_name(READONLY_INPUTS)
                .takes_value(true)
                .required(true)
                .multiple(true),
        )
}
