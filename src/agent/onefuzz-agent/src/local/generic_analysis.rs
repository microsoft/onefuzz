// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_exe, get_hash_map, get_synced_dir, CmdType,
        ANALYSIS_DIR, ANALYZER_ENV, ANALYZER_EXE, ANALYZER_OPTIONS, CRASHES_DIR, TARGET_ENV,
        TARGET_EXE, TARGET_OPTIONS, TOOLS_DIR,
    },
    tasks::{
        analysis::generic::{run as run_analysis, Config},
        config::CommonConfig,
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use storage_queue::QueueClient;

pub fn build_analysis_config(
    args: &clap::ArgMatches<'_>,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_options = get_cmd_arg(CmdType::Target, args);

    let analyzer_exe = value_t!(args, ANALYZER_EXE, String)?;
    let analyzer_options = args.values_of_lossy(ANALYZER_OPTIONS).unwrap_or_default();
    let analyzer_env = get_hash_map(args, ANALYZER_ENV)?;
    let analysis = get_synced_dir(ANALYSIS_DIR, common.job_id, common.task_id, args)?;
    let tools = get_synced_dir(TOOLS_DIR, common.job_id, common.task_id, args)?;
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args).ok();

    let config = Config {
        target_exe,
        target_options,
        crashes,
        input_queue,
        analyzer_exe,
        analyzer_options,
        analyzer_env,
        analysis,
        tools,
        common,
        reports: None,
        unique_reports: None,
        no_repro: None,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let common = build_common_config(args)?;
    let config = build_analysis_config(args, None, common)?;
    run_analysis(config).await
}

pub fn build_shared_args() -> Vec<Arg<'static, 'static>> {
    vec![
        Arg::with_name(TARGET_EXE)
            .long(TARGET_EXE)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_ENV)
            .long(TARGET_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(TARGET_OPTIONS)
            .default_value("{input}")
            .long(TARGET_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(CRASHES_DIR)
            .long(CRASHES_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(ANALYZER_EXE)
            .takes_value(true)
            .required(true),
        Arg::with_name(ANALYZER_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(ANALYZER_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(ANALYSIS_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(TOOLS_DIR).takes_value(true).required(false),
    ]
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only generic analysis")
        .args(&build_shared_args())
}
