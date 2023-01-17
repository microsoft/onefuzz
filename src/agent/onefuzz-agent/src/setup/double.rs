// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::Arc;

use tokio::sync::RwLock;

use super::*;

#[derive(Clone, Debug, Default)]
pub struct SetupRunnerDouble {
    pub ran: Arc<RwLock<Vec<WorkSet>>>,
    pub script: SetupOutput,
    pub error_message: Option<String>,
}

#[async_trait]
impl ISetupRunner for SetupRunnerDouble {
    async fn run(&self, work_set: &WorkSet) -> Result<SetupOutput> {
        let mut ran = self.ran.write().await;
        ran.push(work_set.clone());
        if let Some(error) = self.error_message.clone() {
            anyhow::bail!(error);
        }
        Ok(self.script.clone())
    }
}
