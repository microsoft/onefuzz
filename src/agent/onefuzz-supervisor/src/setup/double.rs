// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct SetupRunnerDouble {
    pub ran: Vec<WorkSet>,
    pub script: bool,
}

#[async_trait]
impl ISetupRunner for SetupRunnerDouble {
    async fn run(&mut self, work_set: &WorkSet) -> Result<bool> {
        self.ran.push(work_set.clone());
        Ok(self.script)
    }
}
