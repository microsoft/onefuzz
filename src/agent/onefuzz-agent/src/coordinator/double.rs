// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Debug, Default)]
pub struct CoordinatorDouble {
    pub commands: Arc<RwLock<Vec<NodeCommand>>>,
    pub events: Arc<RwLock<Vec<NodeEvent>>>,
}

#[async_trait]
impl ICoordinator for CoordinatorDouble {
    async fn poll_commands(&mut self) -> Result<Option<NodeCommand>, PollCommandError> {
        let mut commands = self.commands.write().await;
        Ok(commands.pop())
    }

    async fn emit_event(&self, event: NodeEvent) -> Result<()> {
        let mut events = self.events.write().await;
        events.push(event);
        Ok(())
    }

    async fn can_schedule(&self, _work: &WorkSet) -> Result<CanSchedule> {
        Ok(CanSchedule {
            allowed: true,
            work_stopped: true,
            reason: None,
        })
    }
}
