// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::{
        generic_crash_report::{build_report_config, build_shared_args as build_crash_args},
        generic_generator::{build_fuzz_config, build_shared_args as build_fuzz_args},
    },
    tasks::{fuzz::generator::GeneratorTask, report::generic::ReportTask},
};
use anyhow::Result;
use clap::{App, SubCommand};
use std::collections::HashSet;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let fuzz_config = build_fuzz_config(args)?;
    let report_config = build_report_config(args)?;

    let fuzzer = GeneratorTask::new(fuzz_config);
    let fuzz_task = fuzzer.run();

    let report = ReportTask::new(&report_config);
    let report_task = report.local_run();

    tokio::try_join!(fuzz_task, report_task)?;

    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    let mut app = SubCommand::with_name(name).about("run a local generator & crash reporting job");

    let mut used = HashSet::new();
    for args in &[build_fuzz_args(), build_crash_args()] {
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
