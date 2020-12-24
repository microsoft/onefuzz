// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        add_cmd_options, build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, CmdType,
    },
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
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let inputs = value_t!(args, "inputs", String)?;
    let unique_inputs = value_t!(args, "unique_inputs", String)?;

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
    let mut app = SubCommand::with_name(name).about("execute a local-only libfuzzer merge task");

    app = add_cmd_options(CmdType::Target, true, true, true, app);
    app.arg(Arg::with_name("inputs").takes_value(true).required(true))
        .arg(
            Arg::with_name("unique_inputs")
                .takes_value(true)
                .required(true),
        )
}
