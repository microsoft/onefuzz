// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use onefuzz::machine_id::MachineIdentity;
use schemars::JsonSchema;
use std::{collections::HashMap, path::PathBuf};
use uuid::Uuid;

use super::template::{RunContext, Template};

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
impl Template<TestInput> for TestInput {
    fn example_values() -> TestInput {
        TestInput {
            input: PathBuf::new(),
            target_exe: PathBuf::from("path_to_your_exe"),
            target_options: vec![],
            target_env: HashMap::new(),
            setup_dir: PathBuf::new(),
            extra_setup_dir: None,
            task_id: Uuid::new_v4(),
            job_id: Uuid::new_v4(),
            target_timeout: None,
            check_retry_count: 5,
            check_asan_log: true,
            check_debugger: true,
            minimized_stack_depth: None,
        }
    }
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
