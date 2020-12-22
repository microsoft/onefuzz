// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use clap::{App, SubCommand};

use crate::{debug::libfuzzer_merge, local::common::add_common_config};

const LIBFUZZER_MERGE: &str = "libfuzzer-merge";

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    match args.subcommand() {
        (LIBFUZZER_MERGE, Some(sub)) => libfuzzer_merge::run(sub).await,
        _ => {
            anyhow::bail!("missing subcommand\nUSAGE: {}", args.usage());
        }
    }
}

pub fn args(name: &str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("unsupported internal debugging commands")
        .subcommand(add_common_config(libfuzzer_merge::args(LIBFUZZER_MERGE)))
}
