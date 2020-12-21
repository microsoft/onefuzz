// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::build_common_config,
    tasks::{
        merge::libfuzzer_merge::{merge_inputs, Config},
        utils::parse_key_value,
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::syncdir::SyncedDir;
use std::{collections::HashMap, path::PathBuf, sync::Arc};

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let inputs = value_t!(args, "inputs", String)?;
    let unique_inputs = value_t!(args, "unique_inputs", String)?;
    let target_options = args.values_of_lossy("target_options").unwrap_or_default();

    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }

    let common = build_common_config(args)?;
    let config = Arc::new(Config {
        target_exe,
        target_env,
        target_options,
        input_queue: None,
        inputs: vec![SyncedDir {
            path: inputs.into(),
            url: None,
        }],
        unique_inputs: SyncedDir {
            path: unique_inputs.into(),
            url: None,
        },
        common,
        preserve_existing_outputs: true,
    });

    let results = merge_inputs(config.clone(), vec![config.clone().inputs[0].path.clone()]).await?;
    println!("{:#?}", results);
    Ok(())
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
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
