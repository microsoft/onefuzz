// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{collections::HashMap, path::PathBuf};

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir, CmdType,
        SyncCountDirMonitor, UiEvent, CHECK_FUZZER_HELP, CHECK_RETRY_COUNT, CRASHES_DIR,
        DISABLE_CHECK_QUEUE, NO_REPRO_DIR, REPORTS_DIR, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT, UNIQUE_REPORTS_DIR,
    },
    tasks::{
        config::CommonConfig,
        report::libfuzzer_report::{Config, ReportTask},
        utils::default_bool_true,
    },
};
use anyhow::Result;
use async_trait::async_trait;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use futures::future::OptionFuture;
use schemars::JsonSchema;
use storage_queue::QueueClient;

use super::template::{RunContext, Template};

pub fn build_report_config(
    args: &clap::ArgMatches,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);

    let crashes = get_synced_dir(CRASHES_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;
    let reports = get_synced_dir(REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let no_repro = get_synced_dir(NO_REPRO_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let unique_reports = get_synced_dir(UNIQUE_REPORTS_DIR, common.job_id, common.task_id, args)
        .ok()
        .monitor_count(&event_sender)?;

    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();

    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has a default");

    let check_queue = !args.get_flag(DISABLE_CHECK_QUEUE);

    let check_fuzzer_help = args.get_flag(CHECK_FUZZER_HELP);

    let crashes = if input_queue.is_none() { crashes } else { None };

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_retry_count,
        check_fuzzer_help,
        minimized_stack_depth: None,
        input_queue,
        check_queue,
        crashes,
        reports,
        no_repro,
        unique_reports,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone()).await?;
    let config = build_report_config(args, None, context.common_config.clone(), event_sender)?;
    ReportTask::new(config).managed_run().await
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).long(TARGET_EXE).required(true),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(CRASHES_DIR)
            .long(CRASHES_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(REPORTS_DIR)
            .long(REPORTS_DIR)
            .required(false)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(NO_REPRO_DIR)
            .long(NO_REPRO_DIR)
            .required(false)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(UNIQUE_REPORTS_DIR)
            .long(UNIQUE_REPORTS_DIR)
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64))
            .long(TARGET_TIMEOUT),
        Arg::new(CHECK_RETRY_COUNT)
            .long(CHECK_RETRY_COUNT)
            .value_parser(value_parser!(u64))
            .default_value("0"),
        Arg::new(DISABLE_CHECK_QUEUE)
            .action(ArgAction::SetTrue)
            .long(DISABLE_CHECK_QUEUE),
        Arg::new(CHECK_FUZZER_HELP)
            .action(ArgAction::SetTrue)
            .long(CHECK_FUZZER_HELP),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("execute a local-only libfuzzer crash report task")
        .args(&build_shared_args())
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerCrashReport {
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_timeout: Option<u64>,
    input_queue: Option<PathBuf>,
    crashes: Option<PathBuf>,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,

    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,

    #[serde(default)]
    check_retry_count: u64,

    #[serde(default)]
    minimized_stack_depth: Option<usize>,

    #[serde(default = "default_bool_true")]
    check_queue: bool,
}

#[async_trait]
impl Template for LibfuzzerCrashReport {
    async fn run(&self, context: &RunContext) -> Result<()> {
        let input_q_fut: OptionFuture<_> = self
            .input_queue
            .iter()
            .map(|w| context.monitor_dir(w))
            .next()
            .into();
        let input_q = input_q_fut.await.transpose()?;

        let libfuzzer_crash_config = crate::tasks::report::libfuzzer_report::Config {
            target_exe: self.target_exe.clone(),
            target_env: self.target_env.clone(),
            target_options: self.target_options.clone(),
            target_timeout: self.target_timeout,
            input_queue: input_q,
            crashes: self
                .crashes
                .clone()
                .map(|c| context.to_monitored_sync_dir("crashes", c))
                .transpose()?,
            reports: self
                .reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("reports", c))
                .transpose()?,
            unique_reports: self
                .unique_reports
                .clone()
                .map(|c| context.to_monitored_sync_dir("unique_reports", c))
                .transpose()?,
            no_repro: self
                .no_repro
                .clone()
                .map(|c| context.to_monitored_sync_dir("no_repro", c))
                .transpose()?,

            check_fuzzer_help: self.check_fuzzer_help,
            check_retry_count: self.check_retry_count,
            minimized_stack_depth: self.minimized_stack_depth,
            check_queue: self.check_queue,
            common: CommonConfig {
                task_id: uuid::Uuid::new_v4(),
                ..context.common.clone()
            },
        };

        context
            .spawn(async move {
                let mut libfuzzer_report =
                    crate::tasks::report::libfuzzer_report::ReportTask::new(libfuzzer_crash_config);
                libfuzzer_report.managed_run().await
            })
            .await;
        Ok(())
    }
}
