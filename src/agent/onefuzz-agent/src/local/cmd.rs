// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use clap::{App, SubCommand};
use onefuzz::utils::try_wait_all_join_handles;

use crate::local::{
    common::add_common_config, generic_analysis, generic_crash_report, generic_generator,
    libfuzzer, libfuzzer_coverage, libfuzzer_crash_report, libfuzzer_fuzz, libfuzzer_merge,
    radamsa, tui::TerminalUi,
};

const RADAMSA: &str = "radamsa";
const LIBFUZZER: &str = "libfuzzer";
const LIBFUZZER_FUZZ: &str = "libfuzzer-fuzz";
const LIBFUZZER_CRASH_REPORT: &str = "libfuzzer-crash-report";
const LIBFUZZER_COVERAGE: &str = "libfuzzer-coverage";
const LIBFUZZER_MERGE: &str = "libfuzzer-merge";
const GENERIC_CRASH_REPORT: &str = "generic-crash-report";
const GENERIC_GENERATOR: &str = "generic-generator";
const GENERIC_ANALYSIS: &str = "generic-analysis";

pub async fn run(args: clap::ArgMatches<'static>) -> Result<()> {
    let terminal = TerminalUi::init()?;
    let event_sender = terminal.task_events.clone();
    let command_run = tokio::spawn(async move {
        match args.subcommand() {
            (RADAMSA, Some(sub)) => radamsa::run(sub).await,
            (LIBFUZZER, Some(sub)) => libfuzzer::run(sub, event_sender).await,
            (LIBFUZZER_FUZZ, Some(sub)) => libfuzzer_fuzz::run(sub).await,
            (LIBFUZZER_COVERAGE, Some(sub)) => libfuzzer_coverage::run(sub).await,
            (LIBFUZZER_CRASH_REPORT, Some(sub)) => libfuzzer_crash_report::run(sub).await,
            (LIBFUZZER_MERGE, Some(sub)) => libfuzzer_merge::run(sub).await,
            (GENERIC_ANALYSIS, Some(sub)) => generic_analysis::run(sub).await,
            (GENERIC_CRASH_REPORT, Some(sub)) => generic_crash_report::run(sub).await,
            (GENERIC_GENERATOR, Some(sub)) => generic_generator::run(sub).await,
            _ => {
                anyhow::bail!("missing subcommand\nUSAGE: {}", args.usage());
            }
        }
    });

    let ui_run = tokio::spawn(terminal.run());
    try_wait_all_join_handles(vec![ui_run, command_run]).await?;
    Ok(())
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("pre-release local fuzzing")
        .subcommand(add_common_config(radamsa::args(RADAMSA)))
        .subcommand(add_common_config(libfuzzer::args(LIBFUZZER)))
        .subcommand(add_common_config(libfuzzer_fuzz::args(LIBFUZZER_FUZZ)))
        .subcommand(add_common_config(libfuzzer_coverage::args(
            LIBFUZZER_COVERAGE,
        )))
        .subcommand(add_common_config(libfuzzer_merge::args(LIBFUZZER_MERGE)))
        .subcommand(add_common_config(libfuzzer_crash_report::args(
            LIBFUZZER_CRASH_REPORT,
        )))
        .subcommand(add_common_config(generic_crash_report::args(
            GENERIC_CRASH_REPORT,
        )))
        .subcommand(add_common_config(generic_generator::args(
            GENERIC_GENERATOR,
        )))
        .subcommand(add_common_config(generic_analysis::args(GENERIC_ANALYSIS)))
}
