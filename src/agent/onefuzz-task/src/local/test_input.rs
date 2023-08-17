// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, CmdType, UiEvent, CHECK_ASAN_LOG,
        CHECK_RETRY_COUNT, DISABLE_CHECK_DEBUGGER, TARGET_ENV, TARGET_EXE, TARGET_OPTIONS,
        TARGET_TIMEOUT,
    },
    tasks::report::generic::{test_input, TestInputArgs},
};
use anyhow::Result;
use async_trait::async_trait;
use clap::{Arg, ArgAction, Command};
use flume::Sender;
use onefuzz::machine_id::MachineIdentity;
use schemars::JsonSchema;
use std::{collections::HashMap, path::PathBuf};
use uuid::Uuid;

use super::template::{RunContext, Template};

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, false, event_sender).await?;

    let target_exe = args
        .get_one::<PathBuf>(TARGET_EXE)
        .expect("is marked required");
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let input = args
        .get_one::<PathBuf>("input")
        .expect("is marked required");
    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();
    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has default value");
    let check_asan_log = args.get_flag(CHECK_ASAN_LOG);
    let check_debugger = !args.get_flag(DISABLE_CHECK_DEBUGGER);

    let config = TestInputArgs {
        target_exe: target_exe.as_path(),
        target_env: &target_env,
        target_options: &target_options,
        input_url: None,
        input: input.as_path(),
        job_id: context.common_config.job_id,
        task_id: context.common_config.task_id,
        target_timeout,
        check_retry_count,
        setup_dir: &context.common_config.setup_dir,
        extra_setup_dir: context.common_config.extra_setup_dir.as_deref(),
        minimized_stack_depth: None,
        check_asan_log,
        check_debugger,
        machine_identity: context.common_config.machine_identity.clone(),
    };

    let result = test_input(config).await?;
    println!("{}", serde_json::to_string_pretty(&result)?);
    Ok(())
}

pub fn build_shared_args() -> Vec<Arg> {
    vec![
        Arg::new(TARGET_EXE).required(true),
        Arg::new("input")
            .required(true)
            .value_parser(value_parser!(PathBuf)),
        Arg::new(TARGET_ENV).long(TARGET_ENV).num_args(0..),
        Arg::new(TARGET_OPTIONS)
            .default_value("{input}")
            .long(TARGET_OPTIONS)
            .value_delimiter(' ')
            .help("Use a quoted string with space separation to denote multiple arguments"),
        Arg::new(TARGET_TIMEOUT)
            .long(TARGET_TIMEOUT)
            .value_parser(value_parser!(u64)),
        Arg::new(CHECK_RETRY_COUNT)
            .long(CHECK_RETRY_COUNT)
            .value_parser(value_parser!(u64))
            .default_value("0"),
        Arg::new(CHECK_ASAN_LOG)
            .action(ArgAction::SetTrue)
            .long(CHECK_ASAN_LOG),
        Arg::new(DISABLE_CHECK_DEBUGGER)
            .action(ArgAction::SetTrue)
            .long("disable_check_debugger"),
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("test an application with a specific input")
        .args(&build_shared_args())
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct TestInput {
    input: PathBuf,
    target_exe: PathBuf,
    target_options: Vec<String>,
    target_env: HashMap<String, String>,
    setup_dir: PathBuf,
    extra_setup_dir: Option<PathBuf>,
    task_id: Uuid,
    job_id: Uuid,
    target_timeout: Option<u64>,
    check_retry_count: u64,
    check_asan_log: bool,
    check_debugger: bool,
    minimized_stack_depth: Option<usize>,
}

#[async_trait]
impl Template for TestInput {
    async fn run(&self, context: &RunContext) -> Result<()> {
        let c = self.clone();
        let t = tokio::spawn(async move {
            let libfuzzer_test_input = crate::tasks::report::generic::TestInputArgs {
                input_url: None,
                input: c.input.as_path(),
                target_exe: c.target_exe.as_path(),
                target_options: &c.target_options,
                target_env: &c.target_env,
                setup_dir: &c.setup_dir,
                extra_setup_dir: c.extra_setup_dir.as_deref(),
                task_id: uuid::Uuid::new_v4(),
                job_id: uuid::Uuid::new_v4(),
                target_timeout: c.target_timeout,
                check_retry_count: c.check_retry_count,
                check_asan_log: c.check_asan_log,
                check_debugger: c.check_debugger,
                minimized_stack_depth: c.minimized_stack_depth,
                machine_identity: MachineIdentity {
                    machine_id: uuid::Uuid::new_v4(),
                    machine_name: "local".to_string(),
                    scaleset_name: None,
                },
            };

            crate::tasks::report::generic::test_input(libfuzzer_test_input)
                .await
                .map(|_| ())
        });

        context.add_handle(t).await;
        Ok(())
    }
}
