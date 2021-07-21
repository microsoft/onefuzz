// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Debug, Default)]
pub struct CoordinatorDouble {
    pub commands: Vec<NodeCommand>,
    pub events: Vec<NodeEvent>,
}

#[async_trait]
impl ICoordinator for CoordinatorDouble {
    async fn poll_commands(&mut self) -> Result<PollCommandResult> {
        match self.commands.pop() {
            Some(x) => Ok(PollCommandResult::Command(x)),
            None => Ok(PollCommandResult::None),
        }
    }

    async fn emit_event(&mut self, event: NodeEvent) -> Result<()> {
        self.events.push(event);
        Ok(())
    }

    async fn can_schedule(&mut self, _work: &WorkSet) -> Result<CanSchedule> {
        Ok(CanSchedule {
            allowed: true,
            work_stopped: true,
        })
    }
}
