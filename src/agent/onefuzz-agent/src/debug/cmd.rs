// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use clap::{App, SubCommand};

use crate::{
    debug::{generic_crash_report, libfuzzer_merge},
    local::common::add_common_config,
};

const GENERIC_CRASH_REPORT: &str = "generic-crash-report";
const LIBFUZZER_MERGE: &str = "libfuzzer-merge";

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    match args.subcommand() {
        (GENERIC_CRASH_REPORT, Some(sub)) => generic_crash_report::run(sub).await,
        (LIBFUZZER_MERGE, Some(sub)) => libfuzzer_merge::run(sub).await,
        _ => {
            anyhow::bail!("missing subcommand\nUSAGE: {}", args.usage());
        }
    }
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("unsupported internal debugging commands")
        .subcommand(add_common_config(generic_crash_report::args(
            GENERIC_CRASH_REPORT,
        )))
        .subcommand(add_common_config(libfuzzer_merge::args(LIBFUZZER_MERGE)))
}
