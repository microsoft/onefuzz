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
