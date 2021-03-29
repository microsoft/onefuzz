// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir,
        get_synced_dirs, CmdType, CHECK_FUZZER_HELP, COVERAGE_DIR, INPUTS_DIR, READONLY_INPUTS,
        TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
    },
    tasks::{
        config::CommonConfig,
        coverage::libfuzzer_coverage::{Config, CoverageTask},
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use storage_queue::QueueClient;

pub fn build_coverage_config(
    args: &clap::ArgMatches<'_>,
    standalone_job: bool,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let readonly_inputs = if standalone_job {
        vec![get_synced_dir(
            INPUTS_DIR,
            common.job_id,
            common.task_id,
            args,
        )?]
    } else {
        get_synced_dirs(READONLY_INPUTS, common.job_id, common.task_id, args).unwrap_or_default()
    };

    let coverage = get_synced_dir(COVERAGE_DIR, common.job_id, common.task_id, args)?;
    let check_fuzzer_help = args.is_present(CHECK_FUZZER_HELP);

    let config = Config {
        target_exe,
        target_env,
        target_options,
        check_fuzzer_help,
        input_queue,
        readonly_inputs,
        coverage,
        common,
        check_queue: false,
    };
    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let common = build_common_config(args, true)?;
    let config = build_coverage_config(args, true, None, common)?;

    let mut task = CoverageTask::new(config);
    task.managed_run().await
}

pub fn build_shared_args(standalone_job: bool) -> Vec<Arg<'static, 'static>> {
    let mut args = vec![
        Arg::with_name(TARGET_EXE)
            .long(TARGET_EXE)
            .takes_value(true)
            .required(true),
        Arg::with_name(TARGET_ENV)
            .long(TARGET_ENV)
            .takes_value(true)
            .multiple(true),
        Arg::with_name(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .takes_value(true)
            .value_delimiter(" ")
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::with_name(COVERAGE_DIR)
            .takes_value(true)
            .required(!standalone_job)
            .long(COVERAGE_DIR),
        Arg::with_name(CHECK_FUZZER_HELP)
            .takes_value(false)
            .long(CHECK_FUZZER_HELP),
    ];
    if standalone_job {
        args.push(
            Arg::with_name(INPUTS_DIR)
                .long(INPUTS_DIR)
                .takes_value(true)
                .required(true),
        )
    } else {
        args.push(
            Arg::with_name(READONLY_INPUTS)
                .takes_value(true)
                .required(false)
                .long(READONLY_INPUTS)
                .multiple(true),
        )
    }
    args
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer coverage task")
        .args(&build_shared_args(false))
}
