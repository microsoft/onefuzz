// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    merge::libfuzzer_merge::{merge_inputs, Config},
    utils::parse_key_value,
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{collections::HashMap, path::PathBuf, sync::Arc};
use tokio::runtime::Runtime;
use url::Url;
use uuid::Uuid;

pub fn run(args: &clap::ArgMatches) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let inputs = value_t!(args, "inputs", String)?;
    let unique_inputs = value_t!(args, "unique_inputs", String)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();

    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    let config = Arc::new(Config {
        target_exe,
        target_env,
        target_options,
        input_queue: None,
        inputs: vec![SyncedDir {
            path: inputs.into(),
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/inputs")?)?,
        }],
        unique_inputs: SyncedDir {
            path: unique_inputs.into(),
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/unique_inputs")?)?,
        },
        common: CommonConfig {
            heartbeat_queue: None,
            instrumentation_key: None,
            telemetry_key: None,
            job_id: Uuid::parse_str("00000000-0000-0000-0000-000000000000").unwrap(),
            task_id: Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap(),
            instance_id: Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap(),
        },
        preserve_existing_outputs: true,
    });

    let mut rt = Runtime::new()?;
    rt.block_on(merge_inputs(
        config.clone(),
        vec![config.clone().inputs[0].path.clone()],
    ))?;

    Ok(())
}

pub fn args() -> App<'static, 'static> {
    SubCommand::with_name("libfuzzer-merge")
        .about("execute a local-only libfuzzer merge task")
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(Arg::with_name("inputs").takes_value(true).required(true))
        .arg(
            Arg::with_name("unique_inputs")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("target_env")
                .long("target_env")
                .takes_value(true)
                .multiple(true),
        )
}
