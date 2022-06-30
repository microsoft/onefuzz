// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct WorkerRunnerDouble {
    pub child: ChildDouble,
}

#[async_trait]
impl IWorkerRunner for WorkerRunnerDouble {
    async fn run(&mut self, _setup_dir: &Path, _work: &WorkUnit) -> Result<Box<dyn IWorkerChild>> {
        Ok(Box::new(self.child.clone()))
    }
}

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct ChildDouble {
    pub id: u64,
    pub exit_status: Option<ExitStatus>,
    pub stderr: String,
    pub stdout: String,
    pub killed: bool,
}

impl IWorkerChild for ChildDouble {
    fn try_wait(&mut self) -> Result<Option<Output>> {
        let output = if let Some(exit_status) = self.exit_status {
            Some(Output {
                exit_status,
                stderr: self.stderr.clone(),
                stdout: self.stdout.clone(),
            })
        } else {
            None
        };

        Ok(output)
    }

    fn kill(&mut self) -> Result<()> {
        self.killed = true;
        Ok(())
    }
}
