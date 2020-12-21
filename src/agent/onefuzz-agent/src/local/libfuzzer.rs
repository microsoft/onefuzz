// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        common::{
            add_target_cmd_options, CHECK_RETRY_COUNT, CRASHES_DIR, DISABLE_CHECK_QUEUE,
            INPUTS_DIR, NO_REPRO_DIR, REPORTS_DIR, TARGET_TIMEOUT, TARGET_WORKERS,
            UNIQUE_REPORTS_DIR,
        },
        libfuzzer_crash_report::build_report_config,
        libfuzzer_fuzz::build_fuzz_config,
    },
    tasks::{fuzz::libfuzzer_fuzz::LibFuzzerFuzzTask, report::libfuzzer_report::ReportTask},
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let fuzz_config = build_fuzz_config(args)?;
    let report_config = build_report_config(args)?;

    let fuzzer = LibFuzzerFuzzTask::new(fuzz_config)?;
    let fuzz_task = fuzzer.local_run();

    let report = ReportTask::new(report_config);
    let report_task = report.local_run();

    tokio::try_join!(fuzz_task, report_task)?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local libfuzzer & crash reporting task");

    app = add_target_cmd_options(true, true, true, app);

    app.arg(Arg::with_name(INPUTS_DIR).takes_value(true).required(true))
        .arg(Arg::with_name(CRASHES_DIR).takes_value(true).required(true))
        .arg(
            Arg::with_name(TARGET_WORKERS)
                .long(TARGET_WORKERS)
                .takes_value(true),
        )
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
                .long(TARGET_TIMEOUT),
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
}
