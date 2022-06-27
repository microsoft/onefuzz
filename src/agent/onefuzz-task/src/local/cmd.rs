// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(any(target_os = "linux", target_os = "windows"))]
use crate::local::coverage;
use crate::local::{
    common::add_common_config, dotnet_coverage, generic_analysis, generic_crash_report,
    generic_generator, libfuzzer, libfuzzer_crash_report, libfuzzer_fuzz, libfuzzer_merge,
    libfuzzer_regression, libfuzzer_test_input, radamsa, test_input, tui::TerminalUi,
};
use anyhow::{Context, Result};
use clap::{App, Arg, SubCommand};
use crossterm::tty::IsTty;
use std::str::FromStr;
use std::time::Duration;
use strum::IntoEnumIterator;
use strum_macros::{EnumIter, EnumString, IntoStaticStr};
use tokio::{select, time::timeout};

#[derive(Debug, PartialEq, Eq, EnumString, IntoStaticStr, EnumIter)]
#[strum(serialize_all = "kebab-case")]
enum Commands {
    Radamsa,
    #[cfg(any(target_os = "linux", target_os = "windows"))]
    Coverage,
    DotnetCoverage,
    LibfuzzerFuzz,
    LibfuzzerMerge,
    LibfuzzerCrashReport,
    LibfuzzerTestInput,
    LibfuzzerRegression,
    Libfuzzer,
    CrashReport,
    Generator,
    Analysis,
    TestInput,
}

const TIMEOUT: &str = "timeout";

pub async fn run(args: clap::ArgMatches<'static>) -> Result<()> {
    let running_duration = value_t!(args, TIMEOUT, u64).ok();

    let (cmd, sub_args) = args.subcommand();
    let command =
        Commands::from_str(cmd).with_context(|| format!("unexpected subcommand: {}", cmd))?;

    let sub_args = sub_args
        .ok_or_else(|| anyhow!("missing subcommand arguments"))?
        .to_owned();

    let terminal = if std::io::stdout().is_tty() {
        Some(TerminalUi::init()?)
    } else {
        env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();
        None
    };
    let event_sender = terminal.as_ref().map(|t| t.task_events.clone());
    let command_run = tokio::spawn(async move {
        match command {
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Commands::Coverage => coverage::run(&sub_args, event_sender).await,
            Commands::DotnetCoverage => dotnet_coverage::run(&sub_args, event_sender).await,
            Commands::Radamsa => radamsa::run(&sub_args, event_sender).await,
            Commands::LibfuzzerCrashReport => {
                libfuzzer_crash_report::run(&sub_args, event_sender).await
            }
            Commands::LibfuzzerFuzz => libfuzzer_fuzz::run(&sub_args, event_sender).await,
            Commands::LibfuzzerMerge => libfuzzer_merge::run(&sub_args, event_sender).await,
            Commands::LibfuzzerTestInput => {
                libfuzzer_test_input::run(&sub_args, event_sender).await
            }
            Commands::LibfuzzerRegression => {
                libfuzzer_regression::run(&sub_args, event_sender).await
            }
            Commands::Libfuzzer => libfuzzer::run(&sub_args, event_sender).await,
            Commands::CrashReport => generic_crash_report::run(&sub_args, event_sender).await,
            Commands::Generator => generic_generator::run(&sub_args, event_sender).await,
            Commands::Analysis => generic_analysis::run(&sub_args, event_sender).await,
            Commands::TestInput => test_input::run(&sub_args, event_sender).await,
        }
    });

    if let Some(terminal) = terminal {
        let timeout = running_duration.map(Duration::from_secs);
        let ui_run = tokio::spawn(terminal.run(timeout));
        select! {
            ui_result = ui_run => {
                ui_result??
            },
            command_run_result = command_run => {
                command_run_result??
            }
        };
        Ok(())
    } else if let Some(seconds) = running_duration {
        if let Ok(run) = timeout(Duration::from_secs(seconds), command_run).await {
            run?
        } else {
            info!("The running timeout period has elapsed");
            Ok(())
        }
    } else {
        command_run.await?
    }
}

pub fn args(name: &str) -> App<'static, 'static> {
    let mut cmd = SubCommand::with_name(name)
        .about("pre-release local fuzzing")
        .arg(
            Arg::with_name(TIMEOUT)
                .long(TIMEOUT)
                .help("The maximum running time in seconds")
                .takes_value(true),
        );

    for subcommand in Commands::iter() {
        let app = match subcommand {
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Commands::Coverage => coverage::args(subcommand.into()),
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Commands::DotnetCoverage => dotnet_coverage::args(subcommand.into()),
            Commands::Radamsa => radamsa::args(subcommand.into()),
            Commands::LibfuzzerCrashReport => libfuzzer_crash_report::args(subcommand.into()),
            Commands::LibfuzzerFuzz => libfuzzer_fuzz::args(subcommand.into()),
            Commands::LibfuzzerMerge => libfuzzer_merge::args(subcommand.into()),
            Commands::LibfuzzerTestInput => libfuzzer_test_input::args(subcommand.into()),
            Commands::LibfuzzerRegression => libfuzzer_regression::args(subcommand.into()),
            Commands::Libfuzzer => libfuzzer::args(subcommand.into()),
            Commands::CrashReport => generic_crash_report::args(subcommand.into()),
            Commands::Generator => generic_generator::args(subcommand.into()),
            Commands::Analysis => generic_analysis::args(subcommand.into()),
            Commands::TestInput => test_input::args(subcommand.into()),
        };
        cmd = cmd.subcommand(add_common_config(app));
    }

    cmd
}
