// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::time::Duration;

use anyhow::Result;
use clap::{App, Arg, SubCommand};
use tokio::time::timeout;

use crate::local::{
    common::add_common_config, generic_analysis, generic_crash_report, generic_generator,
    libfuzzer, libfuzzer_coverage, libfuzzer_crash_report, libfuzzer_fuzz, libfuzzer_merge,
    libfuzzer_regression, libfuzzer_test_input, radamsa, test_input,
};

const RADAMSA: &str = "radamsa";
const LIBFUZZER: &str = "libfuzzer";
const LIBFUZZER_FUZZ: &str = "libfuzzer-fuzz";
const LIBFUZZER_CRASH_REPORT: &str = "libfuzzer-crash-report";
const LIBFUZZER_COVERAGE: &str = "libfuzzer-coverage";
const LIBFUZZER_MERGE: &str = "libfuzzer-merge";
const LIBFUZZER_TEST_INPUT: &str = "libfuzzer-test-input";
const LIBFUZZER_REGRESSION: &str = "libfuzzer-regression";
const GENERIC_CRASH_REPORT: &str = "crash-report";
const GENERIC_GENERATOR: &str = "generator";
const GENERIC_ANALYSIS: &str = "analysis";
const GENERIC_TEST_INPUT: &str = "test-input";

const TIMEOUT: &str = "timeout";

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let running_duration = value_t!(args, TIMEOUT, u64).ok();

    let run = async {
        match args.subcommand() {
            (RADAMSA, Some(sub)) => radamsa::run(sub).await,
            (LIBFUZZER, Some(sub)) => libfuzzer::run(sub).await,
            (LIBFUZZER_FUZZ, Some(sub)) => libfuzzer_fuzz::run(sub).await,
            (LIBFUZZER_COVERAGE, Some(sub)) => libfuzzer_coverage::run(sub).await,
            (LIBFUZZER_CRASH_REPORT, Some(sub)) => libfuzzer_crash_report::run(sub).await,
            (LIBFUZZER_MERGE, Some(sub)) => libfuzzer_merge::run(sub).await,
            (GENERIC_ANALYSIS, Some(sub)) => generic_analysis::run(sub).await,
            (GENERIC_CRASH_REPORT, Some(sub)) => generic_crash_report::run(sub).await,
            (GENERIC_GENERATOR, Some(sub)) => generic_generator::run(sub).await,
            (GENERIC_TEST_INPUT, Some(sub)) => test_input::run(sub).await,
            (LIBFUZZER_TEST_INPUT, Some(sub)) => libfuzzer_test_input::run(sub).await,
            (LIBFUZZER_REGRESSION, Some(sub)) => libfuzzer_regression::run(sub).await,
            _ => {
                anyhow::bail!("missing subcommand\nUSAGE: {}", args.usage());
            }
        }
    };

    if let Some(seconds) = running_duration {
        if let Ok(run) = timeout(Duration::from_secs(seconds), run).await {
            run
        } else {
            info!("The running timeout period has elapsed");
            Ok(())
        }
    } else {
        run.await
    }
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("pre-release local fuzzing")
        .arg(
            Arg::with_name(TIMEOUT)
                .long(TIMEOUT)
                .help("The maximum running time in seconds")
                .takes_value(true)
                .required(false),
        )
        .subcommand(add_common_config(radamsa::args(RADAMSA)))
        .subcommand(add_common_config(libfuzzer::args(LIBFUZZER)))
        .subcommand(add_common_config(libfuzzer_fuzz::args(LIBFUZZER_FUZZ)))
        .subcommand(add_common_config(libfuzzer_coverage::args(
            LIBFUZZER_COVERAGE,
        )))
        .subcommand(add_common_config(libfuzzer_merge::args(LIBFUZZER_MERGE)))
        .subcommand(add_common_config(libfuzzer_regression::args(
            LIBFUZZER_REGRESSION,
        )))
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
        .subcommand(add_common_config(test_input::args(GENERIC_TEST_INPUT)))
        .subcommand(add_common_config(libfuzzer_test_input::args(
            LIBFUZZER_TEST_INPUT,
        )))
}
