// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir,
        get_synced_dirs, CmdType, CHECK_FUZZER_HELP, COVERAGE_DIR, INPUTS_DIR, READONLY_INPUTS,
        TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT,
    },
    tasks::{
        config::CommonConfig,
        coverage::generic::{Config, CoverageTask},
    },
};
use anyhow::Result;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use storage_queue::QueueClient;

use super::common::{SyncCountDirMonitor, UiEvent};

pub fn build_coverage_config(
    args: &clap::ArgMatches,
    local_job: bool,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let mut target_options = get_cmd_arg(CmdType::Target, args);
    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();

    let readonly_inputs = if local_job {
        vec![
            get_synced_dir(INPUTS_DIR, common.job_id, common.task_id, args)?
                .monitor_count(&event_sender)?,
        ]
    } else {
        get_synced_dirs(READONLY_INPUTS, common.job_id, common.task_id, args)?
            .into_iter()
            .map(|sd| sd.monitor_count(&event_sender))
            .collect::<Result<Vec<_>>>()?
    };

    let coverage = get_synced_dir(COVERAGE_DIR, common.job_id, common.task_id, args)?
        .monitor_count(&event_sender)?;

    if target_options.is_empty() {
        target_options.push("{input}".to_string());
    }

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        coverage_filter: None,
        module_allowlist: None,
        source_allowlist: None,
        input_queue,
        readonly_inputs,
        coverage,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_coverage_config(
        args,
        false,
        None,
        context.common_config.clone(),
        event_sender,
    )?;

    let mut task = CoverageTask::new(config);
    task.run().await
}

pub fn build_shared_args(local_job: bool) -> Vec<Arg> {
    let mut args = vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .default_value("{input}")
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64))
            .long(TARGET_TIMEOUT),
        Arg::new(COVERAGE_DIR)
            .required(!local_job)
            .value_parser(value_parser!(PathBuf))
            .long(COVERAGE_DIR),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
    ];
    if local_job {
        args.push(
            Arg::new(INPUTS_DIR)
                .long(INPUTS_DIR)
                .required(true)
                .value_parser(value_parser!(PathBuf)),
        )
    } else {
        args.push(
            Arg::new(READONLY_INPUTS)
                .required(true)
                .long(READONLY_INPUTS)
                .value_parser(value_parser!(PathBuf))
                .num_args(1..),
        )
    }
    args
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only coverage task")
        .args(&build_shared_args(false))
}
