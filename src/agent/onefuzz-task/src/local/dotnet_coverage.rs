use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, get_cmd_exe, get_synced_dir,
        get_synced_dirs, CmdType, CHECK_FUZZER_HELP, COVERAGE_DIR, COVERAGE_FILTER, INPUTS_DIR,
        READONLY_INPUTS, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT,
    },
    tasks::{
        config::CommonConfig,
        coverage::dotnet::{Config, CoverageTask}
    }
};

use anyhow::Result;
use clap::{App, Arg, SubCommand};
use flume::Sender;
use storage_queue::QueueClient;

use super::common::{SyncCountDirMonitor, UiEvent};

pub fn build_shared_args(local_job: bool) -> Vec<Arg<'static, 'static>> {
    unimplemented!();
}

pub fn build_coverage_config(
    args: &clap::ArgMatches<'_>,
    local_job: bool,
    input_queue: Option<QueueClient>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<Config> {
    let target_exe = get_cmd_exe(CmdType::Target, args)?.into();
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let target_timeout = value_t!(args, TARGET_TIMEOUT, u64).ok();

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

    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        input_queue,
        readonly_inputs,
        coverage,
        common,
    };

    Ok(config)
}

pub async fn run(args: &clap::ArgMatches<'_>, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender.clone())?;
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

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only coverage task")
        .args(&build_shared_args(false))
}
