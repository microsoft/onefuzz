// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct SetupRunnerDouble {
    pub ran: Vec<WorkSet>,
    pub script: SetupOutput,
    pub error_message: Option<String>,
}

#[async_trait]
impl ISetupRunner for SetupRunnerDouble {
    async fn run(&mut self, work_set: &WorkSet) -> Result<SetupOutput> {
        self.ran.push(work_set.clone());
        if let Some(error) = self.error_message.clone() {
            anyhow::bail!(error);
        }
        Ok(self.script.clone())
    }
}
