// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    report::{libfuzzer_report, generic},
    utils::parse_key_value,
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};
use tokio::runtime::Runtime;
use url::Url;
use uuid::Uuid;

async fn run_impl(input: String, config: Config) -> Result<()> {
    let task = AsanProcessor::new(Arc::new(config)).await?;

    let test_url = Url::parse("https://contoso.com/sample-container/blob.txt")?;
    let input_path = Path::new(&input);
    let result = task.test_input(test_url, &input_path).await;
    println!("{:#?}", result);
    Ok(())
}

pub fn run(args: &clap::ArgMatches) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let setup_dir = value_t!(args, "setup_dir", PathBuf)?;
    let input = value_t!(args, "input", String)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }
    let target_timeout = value_t!(args, "target_timeout", u64).ok();
    let check_retry_count = value_t!(args, "check_retry_count", u64)?;

    // this happens during setup, not during runtime
    let check_fuzzer_help = true;

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_retry_count,
        check_fuzzer_help,
        input_queue: None,
        crashes: None,
        reports: None,
        no_repro: None,
        unique_reports: Some(SyncedDir {
            path: "unique_reports".into(),
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/unique_reports")?)?,
        }),
        common: CommonConfig {
            heartbeat_queue: None,
            instrumentation_key: None,
            telemetry_key: None,
            job_id: Uuid::parse_str("00000000-0000-0000-0000-000000000000").unwrap(),
            task_id: Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap(),
            instance_id: Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap(),
            setup_dir,
        },
    };

    let mut rt = Runtime::new()?;
    rt.block_on(async { run_impl(input, config).await })?;

    Ok(())
}

pub fn args() -> App<'static, 'static> {
    SubCommand::with_name("regression")
        .about("execute a local-only regression task")
        .arg(
            Arg::with_name("setup_dir")
                .takes_value(true)
                .required(false),
        )
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(Arg::with_name("input").takes_value(true).required(true))
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
            Arg::with_name("target_timeout")
                .takes_value(true)
                .long("target_timeout"),
        )
        .arg(
            Arg::with_name("check_retry_count")
                .takes_value(true)
                .long("check_retry_count")
                .default_value("0"),
        )
}
