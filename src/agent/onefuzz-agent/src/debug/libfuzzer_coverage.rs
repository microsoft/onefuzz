// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    coverage::libfuzzer_coverage::{Config, CoverageProcessor},
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
    let mut processor = CoverageProcessor::new(Arc::new(config))
        .await
        .map_err(|e| format_err!("coverage processor failed: {:?}", e))?;
    let input_path = Path::new(&input);
    processor
        .test_input(input_path)
        .await
        .map_err(|e| format_err!("test input failed {:?}", e))?;
    let info = processor
        .total
        .info()
        .await
        .map_err(|e| format_err!("coverage_info failed {:?}", e))?;
    println!("{:?}", info);
    Ok(())
}

pub fn run(args: &clap::ArgMatches) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let input = value_t!(args, "input", String)?;
    let result_dir = value_t!(args, "result_dir", String)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();

    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    // this happens during setup, not during runtime
    let check_fuzzer_help = true;

    let config = Config {
        target_exe,
        target_env,
        target_options,
        check_fuzzer_help,
        input_queue: None,
        readonly_inputs: vec![],
        coverage: SyncedDir {
            path: result_dir.into(),
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/coverage")?)?,
        },
        common: CommonConfig {
            heartbeat_queue: None,
            instrumentation_key: None,
            telemetry_key: None,
            job_id: Uuid::parse_str("00000000-0000-0000-0000-000000000000").unwrap(),
            task_id: Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap(),
            instance_id: Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap(),
            setup_dir: None,
        },
    };

    let mut rt = Runtime::new()?;
    rt.block_on(run_impl(input, config))?;

    Ok(())
}

pub fn args() -> App<'static, 'static> {
    SubCommand::with_name("libfuzzer-coverage")
        .about("execute a local-only libfuzzer coverage task")
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(Arg::with_name("input").takes_value(true).required(true))
        .arg(
            Arg::with_name("result_dir")
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
                .default_value("{input}")
                .help("Supports hyphens.  Recommendation: Set target_env first"),
        )
}
