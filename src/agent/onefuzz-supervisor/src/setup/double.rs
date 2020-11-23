// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct SetupRunnerDouble {
    pub ran: Vec<WorkSet>,
    pub script: SetupOutput,
}

#[async_trait]
impl ISetupRunner for SetupRunnerDouble {
    async fn run(&mut self, work_set: &WorkSet) -> Result<SetupOutput> {
        self.ran.push(work_set.clone());
        Ok(self.script.clone())
    }
}

#[derive(Clone, Debug, Default)]
pub struct FailSetupRunnerDouble {
    error_message: String,
}

impl FailSetupRunnerDouble {
    pub fn new(error_message: String) -> Self {
        Self { error_message }
    }
}

#[async_trait]
impl ISetupRunner for FailSetupRunnerDouble {
    async fn run(&mut self, _work_set: &WorkSet) -> Result<SetupOutput> {
        anyhow::bail!(self.error_message.clone());
    }
}
