// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use clap::{App, SubCommand};

pub fn run(args: &clap::ArgMatches) -> Result<()> {
    match args.subcommand() {
        ("generic-crash-report", Some(sub)) => crate::debug::generic_crash_report::run(sub)?,
        ("libfuzzer-coverage", Some(sub)) => crate::debug::libfuzzer_coverage::run(sub)?,
        ("libfuzzer-crash-report", Some(sub)) => crate::debug::libfuzzer_crash_report::run(sub)?,
        _ => println!("missing subcommand\nUSAGE : {}", args.usage()),
    }

    Ok(())
}

pub fn args() -> App<'static, 'static> {
    SubCommand::with_name("debug")
        .about("unsupported internal debugging commands")
        .subcommand(crate::debug::generic_crash_report::args())
        .subcommand(crate::debug::libfuzzer_coverage::args())
        .subcommand(crate::debug::libfuzzer_crash_report::args())
}
