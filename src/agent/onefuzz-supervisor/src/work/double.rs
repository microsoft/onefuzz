// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct WorkQueueDouble {
    pub available: Vec<Message>,
    pub claimed: Vec<Receipt>,
}

#[async_trait]
impl IWorkQueue for WorkQueueDouble {
    async fn poll(&mut self) -> Result<Option<Message>> {
        Ok(self.available.pop())
    }

    async fn claim(&mut self, receipt: Receipt) -> Result<()> {
        self.claimed.push(receipt);
        Ok(())
    }
}
