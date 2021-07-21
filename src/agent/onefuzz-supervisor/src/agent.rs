// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Error, Result};
use tokio::time;

use crate::coordinator::*;
use crate::done::set_done_lock;
use crate::heartbeat::{AgentHeartbeatClient, HeartbeatSender};
use crate::reboot::*;
use crate::scheduler::*;
use crate::setup::*;
use crate::work::IWorkQueue;
use crate::worker::IWorkerRunner;

const PENDING_COMMANDS_DELAY: time::Duration = time::Duration::from_secs(10);
const BUSY_DELAY: time::Duration = time::Duration::from_secs(1);

pub struct Agent {
    coordinator: Box<dyn ICoordinator>,
    reboot: Box<dyn IReboot>,
    scheduler: Option<Scheduler>,
    setup_runner: Box<dyn ISetupRunner>,
    work_queue: Box<dyn IWorkQueue>,
    worker_runner: Box<dyn IWorkerRunner>,
    heartbeat: Option<AgentHeartbeatClient>,
    previous_state: NodeState,
    last_poll_command: Option<PollCommandResult>,
}

impl Agent {
    pub fn new(
        coordinator: Box<dyn ICoordinator>,
        reboot: Box<dyn IReboot>,
        scheduler: Scheduler,
        setup_runner: Box<dyn ISetupRunner>,
        work_queue: Box<dyn IWorkQueue>,
        worker_runner: Box<dyn IWorkerRunner>,
        heartbeat: Option<AgentHeartbeatClient>,
    ) -> Self {
        let scheduler = Some(scheduler);
        let previous_state = NodeState::Init;
        let last_poll_command = None;

        Self {
            coordinator,
            reboot,
            scheduler,
            setup_runner,
            work_queue,
            worker_runner,
            heartbeat,
            previous_state,
            last_poll_command,
        }
    }

    pub async fn run(&mut self) -> Result<()> {
        let mut instant = time::Instant::now();

        // Tell the service that the agent has started.
        //
        // This must be emitted exactly once per healthy lifetime of a managed
        // node image. The service uses the event to determine when the node has
        // been successfully reimaged and has restarted.
        //
        // If the agent has started up for the first time, the state will be
        // `Free`. If it has started up after a work set-requested reboot, the
        // state will be `Ready`.
        if let Some(Scheduler::Free(..)) = &self.scheduler {
            let event = StateUpdateEvent::Init.into();
            self.coordinator.emit_event(event).await?;
        }

        loop {
            self.heartbeat.alive();
            if instant.elapsed() >= PENDING_COMMANDS_DELAY {
                self.execute_pending_commands().await?;
                instant = time::Instant::now();
            }

            let done = self.update().await?;

            if done {
                debug!("agent done, exiting loop");
                break;
            }
        }

        Ok(())
    }

    async fn update(&mut self) -> Result<bool> {
        let last = self.scheduler.take().ok_or_else(scheduler_error)?;
        let previous_state = NodeState::from(&last);
        let (next, done) = match last {
            Scheduler::Free(s) => (self.free(s).await?, false),
            Scheduler::SettingUp(s) => (self.setting_up(s).await?, false),
            Scheduler::PendingReboot(s) => (self.pending_reboot(s).await?, false),
            Scheduler::Ready(s) => (self.ready(s).await?, false),
            Scheduler::Busy(s) => (self.busy(s).await?, false),
            Scheduler::Done(s) => (self.done(s).await?, true),
        };
        self.previous_state = previous_state;
        self.scheduler = Some(next);
        Ok(done)
    }

    async fn emit_state_update_if_changed(&mut self, event: StateUpdateEvent) -> Result<()> {
        match (&event, self.previous_state) {
            (StateUpdateEvent::Free, NodeState::Free)
            | (StateUpdateEvent::Busy, NodeState::Busy)
            | (StateUpdateEvent::SettingUp { .. }, NodeState::SettingUp)
            | (StateUpdateEvent::Rebooting, NodeState::Rebooting)
            | (StateUpdateEvent::Ready, NodeState::Ready)
            | (StateUpdateEvent::Done { .. }, NodeState::Done) => {}
            _ => {
                self.coordinator.emit_event(event.into()).await?;
            }
        }

        Ok(())
    }

    async fn free(&mut self, state: State<Free>) -> Result<Scheduler> {
        self.emit_state_update_if_changed(StateUpdateEvent::Free)
            .await?;

        let msg = self.work_queue.poll().await?;

        let next = if let Some(msg) = msg {
            info!("received work set message: {:?}", msg);

            let can_schedule = self.coordinator.can_schedule(&msg.work_set).await?;

            if can_schedule.allowed {
                info!("claiming work set: {:?}", msg.work_set);

                match self.work_queue.claim(msg).await {
                    Err(err) => {
                        error!("unable to claim work set: {}", err);

                        // We were unable to claim the work set, so it will reappear in the pool's
                        // work queue when the visibility timeout expires. Don't execute the work,
                        // or else another node will pick it up, and it will be double-scheduled.
                        //
                        // Stay in the `Free` state.
                        state.into()
                    }
                    Ok(work_set) => {
                        info!("claimed work set: {:?}", work_set);

                        // We are allowed to schedule this work, and we have claimed it, so no other
                        // node will see it.
                        //
                        // Transition to `SettingUp` state.
                        let state = state.schedule(work_set);
                        state.into()
                    }
                }
            } else {
                // We cannot schedule the work set. Depending on why, we want to either drop the work
                // (because it is no longer valid for anyone) or do nothing (because our version is out
                // of date, and we want another node to pick it up).
                warn!("unable to schedule work set: {:?}", msg.work_set);

                // If `work_stopped`, the work set is not valid for any node, and we should drop it for the
                // entire pool by claiming but not executing it.
                if can_schedule.work_stopped {
                    match self.work_queue.claim(msg).await {
                        Err(err) => {
                            error!("unable to drop stopped work: {}", err);
                        }
                        Ok(work_set) => {
                            info!("dropped stopped work set: {:?}", work_set);
                        }
                    }
                } else {
                    // Otherwise, the work was not stopped, but we still should not execute it. This is likely
                    // our because agent version is out of date. Do nothing, so another node can see the work.
                    // The service will eventually send us a stop command and reimage our node, if appropriate.
                    debug!(
                        "not scheduling active work set, not dropping: {:?}",
                        msg.work_set
                    );
                }

                // Stay in `Free` state.
                state.into()
            }
        } else {
            info!("no work available");
            self.sleep().await;
            state.into()
        };

        Ok(next)
    }

    async fn setting_up(&mut self, state: State<SettingUp>) -> Result<Scheduler> {
        debug!("agent setting up");

        let tasks = state.work_set().task_ids();
        self.emit_state_update_if_changed(StateUpdateEvent::SettingUp { tasks })
            .await?;

        let scheduler = match state.finish(self.setup_runner.as_mut()).await? {
            SetupDone::Ready(s) => s.into(),
            SetupDone::PendingReboot(s) => s.into(),
            SetupDone::Done(s) => s.into(),
        };

        Ok(scheduler)
    }

    async fn pending_reboot(&mut self, state: State<PendingReboot>) -> Result<Scheduler> {
        debug!("agent pending reboot");
        self.emit_state_update_if_changed(StateUpdateEvent::Rebooting)
            .await?;

        let ctx = state.reboot_context();
        self.reboot.save_context(ctx).await?;
        self.reboot.invoke()?; // noreturn

        unreachable!()
    }

    async fn ready(&mut self, state: State<Ready>) -> Result<Scheduler> {
        debug!("agent ready");
        self.emit_state_update_if_changed(StateUpdateEvent::Ready)
            .await?;
        Ok(state.run().await?.into())
    }

    async fn busy(&mut self, state: State<Busy>) -> Result<Scheduler> {
        self.emit_state_update_if_changed(StateUpdateEvent::Busy)
            .await?;

        // Without this sleep, the `Agent.run` loop turns into an extremely tight loop calling
        // `wait4` of the running agents.  This sleep adds a small window to allow the rest of the
        // system to work.  Emperical testing shows this has a significant reduction in CPU use.
        //
        // TODO: The worker_runner monitoring needs to be turned into something event driven.  Once
        // that is done, this sleep should be removed.
        time::sleep(BUSY_DELAY).await;

        let mut events = vec![];
        let updated = state
            .update(&mut events, self.worker_runner.as_mut())
            .await?;

        for event in events {
            self.coordinator.emit_event(event.into()).await?;
        }

        Ok(updated.into())
    }

    async fn done(&mut self, state: State<Done>) -> Result<Scheduler> {
        debug!("agent done");
        set_done_lock().await?;

        let event = match state.cause() {
            DoneCause::SetupError {
                error,
                script_output,
            } => StateUpdateEvent::Done {
                error: Some(error),
                script_output,
            },
            DoneCause::Stopped | DoneCause::WorkersDone => StateUpdateEvent::Done {
                error: None,
                script_output: None,
            },
        };

        self.emit_state_update_if_changed(event).await?;
        // `Done` is a final state.
        Ok(state.into())
    }

    async fn execute_pending_commands(&mut self) -> Result<()> {
        let result = self.coordinator.poll_commands().await?;

        match &result {
            PollCommandResult::None => {}
            PollCommandResult::Command(cmd) => {
                info!("agent received node command: {:?}", cmd);
                self.scheduler()?.execute_command(cmd).await?;
            }
            PollCommandResult::RequestFailed(err) => {
                error!("error polling the service for commands: {:?}", err);
            }
            PollCommandResult::ClaimFailed(err) => {
                if matches!(
                    self.last_poll_command,
                    Some(PollCommandResult::ClaimFailed(..))
                ) {
                    bail!("repeated command claim attempt failures: {:?}", err);
                }
                error!("error polling the service for commands: {:?}", err);
            }
        }

        self.last_poll_command = Some(result);

        Ok(())
    }

    async fn sleep(&mut self) {
        let delay = time::Duration::from_secs(30);
        time::sleep(delay).await;
    }

    fn scheduler(&mut self) -> Result<&mut Scheduler> {
        self.scheduler.as_mut().ok_or_else(scheduler_error)
    }
}

// The agent owns a `Scheduler`, which it must consume when driving its state
// transitions in `update()`. If `self.scheduler` is ever `None` outside of
// `update()`, then it is a fatal internal error.
fn scheduler_error() -> Error {
    anyhow::anyhow!("internal error accessing agent scheduler")
}

#[cfg(test)]
mod tests;
