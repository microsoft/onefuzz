// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    fuzz::libfuzzer_fuzz::{Config, LibFuzzerFuzzTask},
    utils::parse_key_value,
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{collections::HashMap, path::PathBuf};
use tokio::runtime::Runtime;
use url::Url;
use uuid::Uuid;

async fn run_impl(config: Config) -> Result<()> {
    let fuzzer = LibFuzzerFuzzTask::new(config)?;
    let result = fuzzer.start_fuzzer_monitor(0, None).await?;
    println!("{:#?}", result);
    Ok(())
}

pub fn run(args: &clap::ArgMatches) -> Result<()> {
    let crashes_dir = value_t!(args, "crashes_dir", String)?;
    let inputs_dir = value_t!(args, "inputs_dir", String)?;
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    let readonly_inputs = None;
    let target_workers = Some(1);

    let inputs = SyncedDir {
        path: inputs_dir.into(),
        url: BlobContainerUrl::new(Url::parse("https://contoso.com/inputs")?)?,
    };

    let crashes = SyncedDir {
        path: crashes_dir.into(),
        url: BlobContainerUrl::new(Url::parse("https://contoso.com/crashes")?)?,
    };

    let config = Config {
        inputs,
        readonly_inputs,
        crashes,
        target_exe,
        target_env,
        target_options,
        target_workers,
        common: CommonConfig {
            heartbeat_queue: None,
            instrumentation_key: None,
            telemetry_key: None,
            job_id: Uuid::parse_str("00000000-0000-0000-0000-000000000000").unwrap(),
            task_id: Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap(),
        },
    };

    let mut rt = Runtime::new()?;
    rt.block_on(async { run_impl(config).await })?;

    Ok(())
}

pub fn args() -> App<'static, 'static> {
    SubCommand::with_name("libfuzzer-fuzz")
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
}
