// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::{
        build_local_context, get_cmd_arg, get_cmd_env, CmdType, UiEvent, CHECK_RETRY_COUNT,
        TARGET_ENV, TARGET_EXE, TARGET_OPTIONS, TARGET_TIMEOUT,
    },
    tasks::report::libfuzzer_report::{test_input, TestInputArgs},
};
use anyhow::Result;
use async_trait::async_trait;
use clap::{Arg, Command};
use flume::Sender;
use onefuzz::machine_id::MachineIdentity;
use schemars::JsonSchema;
use std::{collections::HashMap, path::PathBuf};

use super::template::{RunContext, Template};

pub async fn run(args: &clap::ArgMatches, event_sender: Option<Sender<UiEvent>>) -> Result<()> {
    let context = build_local_context(args, true, event_sender).await?;

    let target_exe = args
        .get_one::<PathBuf>(TARGET_EXE)
        .expect("marked as required");
    let target_env = get_cmd_env(CmdType::Target, args)?;
    let target_options = get_cmd_arg(CmdType::Target, args);
    let input = args
        .get_one::<PathBuf>("input")
        .expect("marked as required");
    let target_timeout = args.get_one::<u64>(TARGET_TIMEOUT).copied();
    let check_retry_count = args
        .get_one::<u64>(CHECK_RETRY_COUNT)
        .copied()
        .expect("has a default value");

    let extra_setup_dir = context.common_config.extra_setup_dir.as_deref();
    let extra_output_dir = context
        .common_config
        .extra_output
        .as_ref()
        .map(|x| x.local_path.as_path());

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
        extra_setup_dir,
        extra_output_dir,
        minimized_stack_depth: None,
        machine_identity: context.common_config.machine_identity,
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
    ]
}

pub fn args(name: &'static str) -> Command {
    Command::new(name)
        .about("test a libfuzzer application with a specific input")
        .args(&build_shared_args())
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct LibfuzzerTestInput {
    input: PathBuf,
    target_exe: PathBuf,
    target_options: Vec<String>,
    target_env: HashMap<String, String>,
    setup_dir: PathBuf,
    extra_setup_dir: Option<PathBuf>,
    extra_output_dir: Option<PathBuf>,
    target_timeout: Option<u64>,
    check_retry_count: u64,
    minimized_stack_depth: Option<usize>,
}

#[async_trait]
impl Template<LibfuzzerTestInput> for LibfuzzerTestInput {
    fn example_values() -> LibfuzzerTestInput {
        LibfuzzerTestInput {
            input: PathBuf::new(),
            target_exe: PathBuf::from("path_to_your_exe"),
            target_options: vec![],
            target_env: HashMap::new(),
            setup_dir: PathBuf::new(),
            extra_setup_dir: None,
            extra_output_dir: None,
            target_timeout: None,
            check_retry_count: 5,
            minimized_stack_depth: None,
        }
    }
    async fn run(&self, context: &RunContext) -> Result<()> {
        let c = self.clone();
        let t = tokio::spawn(async move {
            let libfuzzer_test_input = crate::tasks::report::libfuzzer_report::TestInputArgs {
                input_url: None,
                input: c.input.as_path(),
                target_exe: c.target_exe.as_path(),
                target_options: &c.target_options,
                target_env: &c.target_env,
                setup_dir: &c.setup_dir,
                extra_output_dir: c.extra_output_dir.as_deref(),
                extra_setup_dir: c.extra_setup_dir.as_deref(),
                task_id: uuid::Uuid::new_v4(),
                job_id: uuid::Uuid::new_v4(),
                target_timeout: c.target_timeout,
                check_retry_count: c.check_retry_count,
                minimized_stack_depth: c.minimized_stack_depth,
                machine_identity: MachineIdentity {
                    machine_id: uuid::Uuid::new_v4(),
                    machine_name: "local".to_string(),
                    scaleset_name: None,
                },
            };

            crate::tasks::report::libfuzzer_report::test_input(libfuzzer_test_input)
                .await
                .map(|_| ())
        });

        context.add_handle(t).await;
        Ok(())
    }
}
