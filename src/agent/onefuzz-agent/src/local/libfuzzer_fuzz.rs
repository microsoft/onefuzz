// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::build_common_config,
    tasks::{
        fuzz::libfuzzer_fuzz::{Config, LibFuzzerFuzzTask},
        utils::parse_key_value,
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{collections::HashMap, path::PathBuf};
use url::Url;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let crashes_dir = value_t!(args, "crashes_dir", String)?;
    let inputs_dir = value_t!(args, "inputs_dir", String)?;
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let target_workers = value_t!(args, "target_workers", u64).unwrap_or_default();
    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    let readonly_inputs = None;

    let inputs = SyncedDir {
        path: inputs_dir.into(),
        url: BlobContainerUrl::new(Url::parse("https://contoso.com/inputs")?)?,
    };

    let crashes = SyncedDir {
        path: crashes_dir.into(),
        url: BlobContainerUrl::new(Url::parse("https://contoso.com/crashes")?)?,
    };

    let ensemble_sync_delay = None;
    let common = build_common_config(args)?;
    let config = Config {
        inputs,
        readonly_inputs,
        crashes,
        target_exe,
        target_env,
        target_options,
        target_workers,
        ensemble_sync_delay,
        common,
    };

    LibFuzzerFuzzTask::new(config)?.local_run().await
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer crash report task")
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("target_env")
                .long("target_env")
                .takes_value(true)
                .multiple(true),
        )
        .arg(
            Arg::with_name("target_options")
                .long("target_options")
                .takes_value(true)
                .multiple(true)
                .allow_hyphen_values(true)
                .help("Supports hyphens.  Recommendation: Set target_env first"),
        )
        .arg(
            Arg::with_name("inputs_dir")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("crashes_dir")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("target_workers")
                .long("target_workers")
                .takes_value(true),
        )
}
