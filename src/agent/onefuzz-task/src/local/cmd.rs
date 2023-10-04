// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::{create_template, template};
#[cfg(any(target_os = "linux", target_os = "windows"))]
use crate::local::coverage;
use crate::local::{common::add_common_config, libfuzzer_fuzz, tui::TerminalUi};
use anyhow::{Context, Result};

use clap::{Arg, ArgAction, Command};
use std::time::Duration;
use std::{path::PathBuf, str::FromStr};
use strum::IntoEnumIterator;
use strum_macros::{EnumIter, EnumString, IntoStaticStr};
use tokio::{select, time::timeout};
#[derive(Debug, PartialEq, Eq, EnumString, IntoStaticStr, EnumIter)]
#[strum(serialize_all = "kebab-case")]
enum Commands {
    #[cfg(any(target_os = "linux", target_os = "windows"))]
    Coverage,
    LibfuzzerFuzz,
    Template,
    CreateTemplate,
}

const TIMEOUT: &str = "timeout";
const TUI: &str = "tui";

pub async fn run(args: clap::ArgMatches) -> Result<()> {
    let running_duration = args.get_one::<u64>(TIMEOUT).copied();

    let start_ui = args.get_flag(TUI);

    let (cmd, sub_args) = args.subcommand().ok_or_else(|| {
        format_err!(
            "Expected subcommand for 'local'. Use 'local help' to see available subcommands."
        )
    })?;

    let command = Commands::from_str(cmd).with_context(|| {
        format!("Unexpected subcommand: {cmd}. Use 'local help' to see available subcommands.")
    })?;

    let sub_args = sub_args.clone();

    let terminal = if start_ui && command != Commands::CreateTemplate {
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
            Commands::LibfuzzerFuzz => libfuzzer_fuzz::run(&sub_args, event_sender).await,
            Commands::Template => {
                let config = sub_args
                    .get_one::<PathBuf>("config")
                    .expect("is marked required");

                template::launch(config, event_sender).await
            }
            Commands::CreateTemplate => create_template::run(),
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

pub fn args(name: &'static str) -> Command {
    let mut cmd = Command::new(name)
        .about("pre-release local fuzzing")
        .arg(
            Arg::new(TIMEOUT)
                .long(TIMEOUT)
                .value_parser(value_parser!(u64))
                .help("The maximum running time in seconds"),
        )
        .arg(
            Arg::new(TUI)
                .long(TUI)
                .help("Enable the terminal UI")
                .action(ArgAction::SetTrue),
        );

    for subcommand in Commands::iter() {
        let add_common = subcommand != Commands::Template;
        let app = match subcommand {
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Commands::Coverage => coverage::args(subcommand.into()),
            Commands::LibfuzzerFuzz => libfuzzer_fuzz::args(subcommand.into()),
            Commands::Template => Command::new("template")
                .about("uses the template to generate a run")
                .args(vec![Arg::new("config")
                    .value_parser(value_parser!(std::path::PathBuf))
                    .required(true)]),
            Commands::CreateTemplate => create_template::args(subcommand.into()),
        };

        cmd = if add_common {
            cmd.subcommand(add_common_config(app))
        } else {
            cmd.subcommand(app)
        }
    }

    cmd
}
