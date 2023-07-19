// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use ipc_channel::ipc;

use super::*;

#[derive(Clone, Debug, Default)]
pub struct WorkerRunnerDouble {
    pub child: ChildDouble,
}

#[async_trait]
impl IWorkerRunner for WorkerRunnerDouble {
    async fn run(
        &self,
        _setup_dir: &Path,
        _extra_setup_dir: Option<PathBuf>,
        _work: &WorkUnit,
        from_agent_to_task_endpoint: String,
        from_task_to_agent_endpoint: String,
    ) -> Result<Box<dyn IWorkerChild>> {
        info!("Creating channel from agent to task");
        let (agent_sender, _receive_from_agent): (
            IpcSender<IpcMessageKind>,
            IpcReceiver<IpcMessageKind>,
        ) = ipc::channel()?;
        info!("Conecting...");
        let oneshot_sender = IpcSender::connect(from_agent_to_task_endpoint)?;
        info!("Sending sender to agent");
        oneshot_sender.send(agent_sender)?;

        info!("Creating channel from task to agent");
        let (_task_sender, receive_from_task): (
            IpcSender<IpcMessageKind>,
            IpcReceiver<IpcMessageKind>,
        ) = ipc::channel()?;
        info!("Connecting...");
        let oneshot_receiver = IpcSender::connect(from_task_to_agent_endpoint)?;
        info!("Sending receiver to agent");
        oneshot_receiver.send(receive_from_task)?;

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
