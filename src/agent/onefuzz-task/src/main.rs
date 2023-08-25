// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate clap;
#[macro_use]
extern crate onefuzz_telemetry;
extern crate onefuzz;

use anyhow::Result;
use clap::{ArgMatches, Command};
use std::io::{stdout, Write};

mod local;
mod managed;
mod tasks;

const LICENSE_CMD: &str = "licenses";
const LOCAL_CMD: &str = "local";
const MANAGED_CMD: &str = "managed";

fn main() -> Result<()> {
    let built_version = format!(
        "{} onefuzz:{} git:{}",
        crate_version!(),
        env!("ONEFUZZ_VERSION"),
        env!("GIT_VERSION")
    );

    let app = Command::new("onefuzz-task")
        .version(built_version)
        .subcommand(managed::cmd::args(MANAGED_CMD))
        .subcommand(local::cmd::args(LOCAL_CMD))
        .subcommand(Command::new(LICENSE_CMD).about("display third-party licenses"));

    let matches = app.get_matches();

    let rt = tokio::runtime::Runtime::new()?;
    let result = rt.block_on(run(matches));
    atexit::execute();
    rt.shutdown_background();
    result
}

async fn run(args: ArgMatches) -> Result<()> {
    // It'd be best to initialize these environment vars in the same abstraction that
    // pulls in user-provided task vars that set the environment, e.g. `target_env`.
    // For now, just ensure that sanitizer environment vars will be inherited by child
    // processes of the task worker (still allowing user overrides).
    set_sanitizer_env_vars()?;

    match args.subcommand() {
        Some((LICENSE_CMD, _)) => licenses(),
        Some((LOCAL_CMD, sub)) => local::cmd::run(sub.to_owned()).await,
        Some((MANAGED_CMD, sub)) => managed::cmd::run(sub).await,
        _ => anyhow::bail!("No command provided. Run with 'help' to see available commands."),
    }
}

fn licenses() -> Result<()> {
    stdout().write_all(include_bytes!("../../data/licenses.json"))?;
    Ok(())
}

fn set_sanitizer_env_vars() -> Result<()> {
    let sanitizer_env_vars = onefuzz::sanitizer::default_sanitizer_env_vars()?;

    for (k, v) in sanitizer_env_vars {
        std::env::set_var(k, v);
    }

    Ok(())
}
