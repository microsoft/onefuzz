// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::build_common_config,
    tasks::{
        report::generic::{Config, GenericReportProcessor},
        utils::parse_key_value,
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
use url::Url;

async fn run_impl(input: String, config: Config) -> Result<()> {
    let input_path = Path::new(&input);
    let test_url = Url::parse("https://contoso.com/sample-container/blob.txt")?;
    let heartbeat_client = config.common.init_heartbeat().await?;
    let processor = GenericReportProcessor::new(&config, heartbeat_client);
    let result = processor.test_input(test_url, input_path).await?;
    println!("{:#?}", result);
    Ok(())
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let input = value_t!(args, "input", String)?;
    let target_timeout = value_t!(args, "target_timeout", u64).ok();
    let check_retry_count = value_t!(args, "check_retry_count", u64)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let check_asan_log = args.is_present("check_asan_log");
    let check_debugger = !args.is_present("disable_check_debugger");

    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    let common = build_common_config(args)?;

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_asan_log,
        check_debugger,
        check_retry_count,
        crashes: None,
        input_queue: None,
        no_repro: None,
        reports: None,
        unique_reports: SyncedDir {
            path: "unique_reports".into(),
            url: BlobContainerUrl::new(url::Url::parse("https://contoso.com/unique_reports")?)?,
        },
        common,
    };

    run_impl(input, config).await
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only generic crash report")
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(Arg::with_name("input").takes_value(true).required(true))
        .arg(
            Arg::with_name("disable_check_debugger")
                .takes_value(false)
                .long("disable_check_debugger"),
        )
        .arg(
            Arg::with_name("check_asan_log")
                .takes_value(false)
                .long("check_asan_log"),
        )
        .arg(
            Arg::with_name("check_retry_count")
                .takes_value(true)
                .long("check_retry_count")
                .default_value("0"),
        )
        .arg(
            Arg::with_name("target_timeout")
                .takes_value(true)
                .long("target_timeout")
                .default_value("5"),
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
                .default_value("{input}")
                .help("Supports hyphens.  Recommendation: Set target_env first"),
        )
}
