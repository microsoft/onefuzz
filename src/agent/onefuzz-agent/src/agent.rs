// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::too_many_arguments)]
use std::time::Duration;

use anyhow::{Error, Result};
use tokio::time;

use crate::coordinator::*;
use crate::done::set_done_lock;
use crate::heartbeat::{AgentHeartbeatClient, HeartbeatSender};
use crate::reboot::*;
use crate::scheduler::*;
use crate::setup::*;
use crate::work::IWorkQueue;
use crate::worker::{IWorkerRunner, WorkerEvent};

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
    last_poll_command: Result<Option<NodeCommand>, PollCommandError>,
    managed: bool,
    machine_id: uuid::Uuid,
    sleep_duration: Duration,
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
        managed: bool,
        machine_id: uuid::Uuid,
    ) -> Self {
        let scheduler = Some(scheduler);
        let previous_state = NodeState::Init;
        let last_poll_command = Ok(None);

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
            managed,
            machine_id,
            sleep_duration: Duration::from_secs(30),
        }
    }

    pub async fn run(self) -> Result<()> {
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
        let mut state = self;
        let mut done = false;
        while !done {
            state.heartbeat.alive();
            if instant.elapsed() >= PENDING_COMMANDS_DELAY {
                state = state.execute_pending_commands().await?;
                instant = time::Instant::now();
            }

            (state, done) = state.update().await?;
        }

        info!("agent done, exiting loop");
        Ok(())
    }

    async fn update(mut self) -> Result<(Self, bool)> {
        let last = self.scheduler.take().ok_or_else(scheduler_error)?;
        let previous_state = NodeState::from(&last);
        let (next, done) = match last {
            Scheduler::Free(s) => (self.free(s, previous_state).await?, false),
            Scheduler::SettingUp(s) => (self.setting_up(s, previous_state).await?, false),
            Scheduler::PendingReboot(s) => (self.pending_reboot(s, previous_state).await?, false),
            Scheduler::Ready(s) => (self.ready(s, previous_state).await?, false),
            Scheduler::Busy(s) => (self.busy(s, previous_state).await?, false),
            //todo: introduce  a new prameter to allow the agent to restart after this point
            Scheduler::Done(s) => (self.done(s, previous_state).await?, true),
        };

        Ok((next, done))
    }

    async fn emit_state_update_if_changed(&self, event: StateUpdateEvent) -> Result<()> {
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

    async fn free(mut self, state: State<Free>, previous: NodeState) -> Result<Self> {
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
                let reason = can_schedule.reason.map_or("".to_string(), |r| r);
                // We cannot schedule the work set. Depending on why, we want to either drop the work
                // (because it is no longer valid for anyone) or do nothing (because our version is out
                // of date, and we want another node to pick it up).
                warn!(
                    "unable to schedule work set: {:?}, Reason {}",
                    msg.work_set, reason
                );

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
                    info!(
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

        Ok(Self {
            previous_state: previous,
            scheduler: Some(next),
            ..self
        })
    }

    async fn setting_up(mut self, state: State<SettingUp>, previous: NodeState) -> Result<Self> {
        info!("agent setting up");

        let tasks = state
            .work_set()
            .work_units
            .iter()
            .map(|w| SettingUpData {
                job_id: w.job_id,
                task_id: w.task_id,
            })
            .collect();

        self.emit_state_update_if_changed(StateUpdateEvent::SettingUp { task_data: tasks })
            .await?;

        let scheduler = match state.finish(self.setup_runner.as_mut()).await? {
            SetupDone::Ready(s) => s.into(),
            SetupDone::PendingReboot(s) => s.into(),
            SetupDone::Done(s) => s.into(),
        };

        Ok(Self {
            previous_state: previous,
            scheduler: Some(scheduler),
            ..self
        })
    }

    async fn pending_reboot(
        self,
        state: State<PendingReboot>,
        _previous: NodeState,
    ) -> Result<Self> {
        info!("agent pending reboot");
        self.emit_state_update_if_changed(StateUpdateEvent::Rebooting)
            .await?;

        let ctx = state.reboot_context();
        self.reboot.save_context(ctx).await?;
        self.reboot.invoke()?; // noreturn

        unreachable!()
    }

    async fn ready(self, state: State<Ready>, previous: NodeState) -> Result<Self> {
        info!("agent ready");
        self.emit_state_update_if_changed(StateUpdateEvent::Ready)
            .await?;
        Ok(Self {
            previous_state: previous,

            scheduler: Some(state.run(self.machine_id).await?.into()),
            ..self
        })
    }

    async fn busy(mut self, state: State<Busy>, previous: NodeState) -> Result<Self> {
        self.emit_state_update_if_changed(StateUpdateEvent::Busy)
            .await?;

        // Without this sleep, the `Agent.run` loop turns into an extremely tight loop calling
        // `wait4` of the running agents.  This sleep adds a small window to allow the rest of the
        // system to work.  Emperical testing shows this has a significant reduction in CPU use.
        //
        // TODO: The worker_runner monitoring needs to be turned into something event driven.  Once
        // that is done, this sleep should be removed.
        time::sleep(BUSY_DELAY).await;

        let mut events: Vec<WorkerEvent> = vec![];
        let updated = state
            .update(&mut events, self.worker_runner.as_mut())
            .await?;

        for event in events {
            self.coordinator.emit_event(event.into()).await?;
        }

        Ok(Self {
            previous_state: previous,
            scheduler: Some(updated.into()),
            ..self
        })
    }

    async fn done(self, state: State<Done>, previous: NodeState) -> Result<Self> {
        info!("agent done");
        set_done_lock(self.machine_id).await?;

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
        Ok(Self {
            previous_state: previous,
            scheduler: Some(state.into()),
            ..self
        })
    }

    async fn execute_pending_commands(mut self) -> Result<Self> {
        let result = self.coordinator.poll_commands().await;

        match &result {
            Ok(None) => Ok(Self {
                last_poll_command: result,
                ..self
            }),
            Ok(Some(cmd)) => {
                info!("agent received node command: {:?}", cmd);
                let managed = self.managed;
                let scheduler = self.scheduler.take().ok_or_else(scheduler_error)?;
                let new_scheduler = scheduler.execute_command(cmd.clone(), managed).await?;

                Ok(Self {
                    last_poll_command: result,
                    scheduler: Some(new_scheduler),
                    ..self
                })
            }
            Err(PollCommandError::RequestFailed(err)) => {
                // If we failed to request commands, this could be the service
                // could be down.  Log it, but keep going.
                error!("error polling the service for commands: {:?}", err);
                Ok(Self {
                    last_poll_command: result,
                    ..self
                })
            }
            Err(PollCommandError::RequestParseFailed(err)) => {
                bail!("poll commands failed: {:?}", err);
            }
            Err(PollCommandError::ClaimFailed(err)) => {
                // If we failed to claim two commands in a row, it means the
                // service is up (since we received the commands we're trying to
                // claim), but something else is going wrong, consistently. This
                // is suspicious, and less likely to be a transient service or
                // networking error, so bail.
                if matches!(
                    self.last_poll_command,
                    Err(PollCommandError::ClaimFailed(..))
                ) {
                    bail!("repeated command claim attempt failures: {:?}", err);
                }
                error!("error claiming command from the service: {:?}", err);
                Ok(Self {
                    last_poll_command: result,
                    ..self
                })
            }
        }
    }

    async fn sleep(&self) {
        time::sleep(self.sleep_duration).await;
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
