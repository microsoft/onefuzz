// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Default)]
pub struct WorkQueueDouble {
    pub available: Vec<Message>,
    pub claimed: Vec<Message>,
}

#[async_trait]
impl IWorkQueue for WorkQueueDouble {
    async fn poll(&mut self) -> Result<Option<Message>> {
        Ok(self.available.pop())
    }

    async fn claim(&mut self, message: Message) -> Result<WorkSet> {
        let work_set = message.work_set.clone();
        self.claimed.push(message);
        Ok(work_set)
    }
}
