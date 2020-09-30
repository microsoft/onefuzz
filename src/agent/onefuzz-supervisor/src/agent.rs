// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Error, Result};
use tokio::time;

use crate::coordinator::*;
use crate::reboot::*;
use crate::scheduler::*;
use crate::setup::*;
use crate::work::IWorkQueue;
use crate::worker::IWorkerRunner;

pub struct Agent {
    coordinator: Box<dyn ICoordinator>,
    reboot: Box<dyn IReboot>,
    scheduler: Option<Scheduler>,
    setup_runner: Box<dyn ISetupRunner>,
    work_queue: Box<dyn IWorkQueue>,
    worker_runner: Box<dyn IWorkerRunner>,
}

impl Agent {
    pub fn new(
        coordinator: Box<dyn ICoordinator>,
        reboot: Box<dyn IReboot>,
        scheduler: Scheduler,
        setup_runner: Box<dyn ISetupRunner>,
        work_queue: Box<dyn IWorkQueue>,
        worker_runner: Box<dyn IWorkerRunner>,
    ) -> Self {
        let scheduler = Some(scheduler);

        Self {
            coordinator,
            reboot,
            scheduler,
            setup_runner,
            work_queue,
            worker_runner,
        }
    }

    pub async fn run(&mut self) -> Result<()> {
        let mut delay = command_delay();

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
            if delay.is_elapsed() {
                self.execute_pending_commands().await?;
                delay = command_delay();
            }

            let done = self.update().await?;

            if done {
                verbose!("agent done, exiting loop");
                break;
            }
        }

        Ok(())
    }

    async fn update(&mut self) -> Result<bool> {
        let last = self.scheduler.take().ok_or_else(scheduler_error)?;

        let next = match last {
            Scheduler::Free(s) => self.free(s).await?,
            Scheduler::SettingUp(s) => self.setting_up(s).await?,
            Scheduler::PendingReboot(s) => self.pending_reboot(s).await?,
            Scheduler::Ready(s) => self.ready(s).await?,
            Scheduler::Busy(s) => self.busy(s).await?,
            Scheduler::Done(s) => self.done(s).await?,
        };

        let done = matches!(next, Scheduler::Done(..));

        self.scheduler = Some(next);

        Ok(done)
    }

    async fn free(&mut self, state: State<Free>) -> Result<Scheduler> {
        let event = StateUpdateEvent::Free.into();
        self.coordinator.emit_event(event).await?;

        let msg = self.work_queue.poll().await?;

        let next = if let Some(msg) = msg {
            verbose!("received work set message: {:?}", msg);

            let can_schedule = self.coordinator.can_schedule(&msg.work_set).await?;

            if can_schedule.allowed {
                info!("claiming work set: {:?}", msg.work_set);

                let claim = self.work_queue.claim(msg.receipt).await;

                if let Err(err) = claim {
                    error!("unable to claim work set: {}", err);

                    // We were unable to claim the work set, so it will reappear in the pool's
                    // work queue when the visibility timeout expires. Don't execute the work,
                    // or else another node will pick it up, and it will be double-scheduled.
                    //
                    // Stay in the `Free` state.
                    state.into()
                } else {
                    info!("claimed work set: {:?}", msg.work_set);

                    // We are allowed to schedule this work, and we have claimed it, so no other
                    // node will see it.
                    //
                    // Transition to `SettingUp` state.
                    let state = state.schedule(msg.work_set.clone());
                    state.into()
                }
            } else {
                // We cannot schedule the work set. Depending on why, we want to either drop the work
                // (because it is no longer valid for anyone) or do nothing (because our version is out
                // of date, and we want another node to pick it up).
                warn!("unable to schedule work set: {:?}", msg.work_set);

                // If `work_stopped`, the work set is not valid for any node, and we should drop it for the
                // entire pool by claiming but not executing it.
                if can_schedule.work_stopped {
                    if let Err(err) = self.work_queue.claim(msg.receipt).await {
                        error!("unable to drop stopped work: {}", err);
                    } else {
                        info!("dropped stopped work set: {:?}", msg.work_set);
                    }
                } else {
                    // Otherwise, the work was not stopped, but we still should not execute it. This is likely
                    // our because agent version is out of date. Do nothing, so another node can see the work.
                    // The service will eventually send us a stop command and reimage our node, if appropriate.
                    verbose!(
                        "not scheduling active work set, not dropping: {:?}",
                        msg.work_set
                    );
                }

                // Stay in `Free` state.
                state.into()
            }
        } else {
            self.sleep().await;
            state.into()
        };

        Ok(next)
    }

    async fn setting_up(&mut self, state: State<SettingUp>) -> Result<Scheduler> {
        verbose!("agent setting up");

        let tasks = state.work_set().task_ids();
        let event = StateUpdateEvent::SettingUp { tasks };
        self.coordinator.emit_event(event.into()).await?;

        let scheduler = match state.finish(self.setup_runner.as_mut()).await? {
            SetupDone::Ready(s) => s.into(),
            SetupDone::PendingReboot(s) => s.into(),
            SetupDone::Done(s) => s.into(),
        };

        Ok(scheduler)
    }

    async fn pending_reboot(&mut self, state: State<PendingReboot>) -> Result<Scheduler> {
        verbose!("agent pending reboot");

        let event = StateUpdateEvent::Rebooting.into();
        self.coordinator.emit_event(event).await?;

        let ctx = state.reboot_context();
        self.reboot.save_context(ctx).await?;
        self.reboot.invoke()?; // noreturn

        unreachable!()
    }

    async fn ready(&mut self, state: State<Ready>) -> Result<Scheduler> {
        verbose!("agent ready");

        let event = StateUpdateEvent::Ready.into();
        self.coordinator.emit_event(event).await?;

        Ok(state.run().await?.into())
    }

    async fn busy(&mut self, state: State<Busy>) -> Result<Scheduler> {
        let event = StateUpdateEvent::Busy.into();
        self.coordinator.emit_event(event).await?;

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
        verbose!("agent done");

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

        let event = event.into();
        self.coordinator.emit_event(event).await?;

        // `Done` is a final state.
        Ok(state.into())
    }

    async fn execute_pending_commands(&mut self) -> Result<()> {
        let cmd = self.coordinator.poll_commands().await?;

        if let Some(cmd) = cmd {
            verbose!("agent received node command: {:?}", cmd);
            self.scheduler()?.execute_command(cmd)?;
        }

        Ok(())
    }

    async fn sleep(&mut self) {
        let delay = time::Duration::from_secs(2);
        time::delay_for(delay).await;
    }

    fn scheduler(&mut self) -> Result<&mut Scheduler> {
        self.scheduler.as_mut().ok_or_else(scheduler_error)
    }
}

fn command_delay() -> time::Delay {
    let delay = time::Duration::from_secs(10);
    time::delay_for(delay)
}

// The agent owns a `Scheduler`, which it must consume when driving its state
// transitions in `update()`. If `self.scheduler` is ever `None` outside of
// `update()`, then it is a fatal internal error.
fn scheduler_error() -> Error {
    anyhow::anyhow!("internal error accessing agent scheduler")
}

#[cfg(test)]
mod tests;
