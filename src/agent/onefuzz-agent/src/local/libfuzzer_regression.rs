// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_common_config, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        CHECK_FUZZER_HELP, CHECK_RETRY_COUNT, COVERAGE_DIR, CRASHES_DIR, NO_REPRO_DIR,
        REGRESSION_REPORTS_DIR, REPORTS_DIR, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::{
        config::CommonConfig,
        regression::libfuzzer::{Config, LibFuzzerRegressionTask},
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};

const REPORT_NAMES: &str = "report_names";

pub fn build_regression_config(
    args: &clap::ArgMatches<'_>,
    common: CommonConfig,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)?;
    let regression_reports =
        get_synced_dir(REGRESSION_REPORTS_DIR, common.job_id, common.task_id, args)?;
    let check_retry_count = value_t!(args, CHECK_RETRY_COUNT, u64)?;

    let reports = get_synced_dir(REPORTS_DIR, common.job_id, common.task_id, args).ok();
    let no_repro = get_synced_dir(NO_REPRO_DIR, common.job_id, common.task_id, args).ok();
    let unique_reports =
        get_synced_dir(UNIQUE_REPORTS_DIR, common.job_id, common.task_id, args).ok();

    let report_list = if args.is_present(REPORT_NAMES) {
        Some(values_t!(args, REPORT_NAMES, String)?)
    } else {
        None
    };

    let check_fuzzer_help = args.is_present(CHECK_FUZZER_HELP);

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_fuzzer_help,
        check_retry_count,
        crashes,
        regression_reports,
        reports,
        no_repro,
        unique_reports,
        readonly_inputs: None,
        report_list,
        minimized_stack_depth: None,
        common,
    };
    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let common = build_common_config(args, true)?;
    let config = build_regression_config(args, common)?;
    LibFuzzerRegressionTask::new(config).run().await
}

pub fn build_shared_args(local_job: bool) -> Vec<Arg<'static, 'static>> {
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
            .required(!local_job)
            .long(COVERAGE_DIR),
        Arg::with_name(CHECK_FUZZER_HELP)
            .takes_value(false)
            .long(CHECK_FUZZER_HELP),
        Arg::with_name(TARGET_TIMEOUT)
            .takes_value(true)
            .long(TARGET_TIMEOUT),
        Arg::with_name(CRASHES_DIR)
            .long(CRASHES_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(REGRESSION_REPORTS_DIR)
            .long(REGRESSION_REPORTS_DIR)
            .takes_value(true)
            .required(local_job),
        Arg::with_name(REPORTS_DIR)
            .long(REPORTS_DIR)
            .takes_value(true)
            .required(false),
        Arg::with_name(NO_REPRO_DIR)
            .long(NO_REPRO_DIR)
            .takes_value(true)
            .required(false),
        Arg::with_name(UNIQUE_REPORTS_DIR)
            .long(UNIQUE_REPORTS_DIR)
            .takes_value(true)
            .required(true),
        Arg::with_name(CHECK_RETRY_COUNT)
            .takes_value(true)
            .long(CHECK_RETRY_COUNT)
            .default_value("0"),
    ];
    if local_job {
        args.push(
            Arg::with_name(REPORT_NAMES)
                .long(REPORT_NAMES)
                .takes_value(true)
                .multiple(true),
        )
    }
    args
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer regression task")
        .args(&build_shared_args(false))
}
