// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        SyncCountDirMonitor, UiEvent, CHECK_FUZZER_HELP, CRASHDUMPS_DIR, CRASHES_DIR, INPUTS_DIR,
        TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_WORKERS,
    },
    tasks::{
        config::CommonConfig,
        fuzz::libfuzzer::generic::{Config, LibFuzzerFuzzTask},
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;

const EXPECT_CRASH_ON_FAILURE: &str = "expect_crash_on_failure";

pub fn build_fuzz_config(
    args: &clap::ArgMatches,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;
    let crashdumps = get_synced_dir(CRASHDUMPS_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;
    let inputs = get_synced_dir(INPUTS_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;

    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let target_workers = args
        .get_one::<usize>("target_workers")
        .copied()
        .unwrap_or_default();

    let readonly_inputs = None;
    let check_fuzzer_help = args.get_flag(CHECK_FUZZER_HELP);
    let expect_crash_on_failure = args.get_flag(EXPECT_CRASH_ON_FAILURE);

    let ensemble_sync_delay = None;

    let config = Config {
        inputs,
        readonly_inputs,
        crashes,
        crashdumps,
        target_exe,
        target_env,
        target_options,
        target_workers,
        ensemble_sync_delay,
        check_fuzzer_help,
        expect_crash_on_failure,
        common,
        extra: (),
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_fuzz_config(args, context.common_config.clone(), event_sender)?;
    LibFuzzerFuzzTask::new(config)?.run().await
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(INPUTS_DIR)
            .long(INPUTS_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(CRASHES_DIR)
            .long(CRASHES_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(CRASHDUMPS_DIR)
            .long(CRASHDUMPS_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(TARGET_WORKERS)
            .long(TARGET_WORKERS)
            .value_parser(value_parser!(u64)),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
        Arg::new(EXPECT_CRASH_ON_FAILURE)
            .action(ArgAction::SetTrue)
            .long(EXPECT_CRASH_ON_FAILURE),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only libfuzzer fuzzing task")
        .args(&build_shared_args())
}
