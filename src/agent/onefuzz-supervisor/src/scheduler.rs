// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fmt;

use anyhow::Result;

use crate::coordinator::NodeCommand;
use crate::reboot::RebootContext;
use crate::setup::ISetupRunner;
use crate::work::*;
use crate::worker::*;

pub enum Scheduler {
    Free(State<Free>),
    SettingUp(State<SettingUp>),
    PendingReboot(State<PendingReboot>),
    Ready(State<Ready>),
    Busy(State<Busy>),
    Done(State<Done>),
}

impl fmt::Display for Scheduler {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        let s = match self {
            Self::Free(..) => "Scheduler::Free",
            Self::SettingUp(..) => "Scheduler::SettingUp",
            Self::PendingReboot(..) => "Scheduler::PendingReboot",
            Self::Ready(..) => "Scheduler::Ready",
            Self::Busy(..) => "Scheduler::Busy",
            Self::Done(..) => "Scheduler::Done",
        };
        write!(f, "{}", s)
    }
}

impl Scheduler {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn execute_command(&mut self, cmd: NodeCommand) -> Result<()> {
        match cmd {
            NodeCommand::StopTask(stop_task) => {
                if let Scheduler::Busy(state) = self {
                    state.stop(stop_task.task_id)?;
                }
            }
        }

        Ok(())
    }
}

impl Default for Scheduler {
    fn default() -> Self {
        let state = State { ctx: Free };
        state.into()
    }
}

pub struct Free;

pub struct SettingUp {
    work_set: WorkSet,
}

pub struct PendingReboot {
    work_set: WorkSet,
}

pub struct Ready {
    work_set: WorkSet,
}

pub struct Busy {
    workers: Vec<Option<Worker>>,
}

pub struct Done;

pub trait Context {}

impl Context for Free {}
impl Context for SettingUp {}
impl Context for PendingReboot {}
impl Context for Ready {}
impl Context for Busy {}
impl Context for Done {}

pub struct State<C: Context> {
    ctx: C,
}

macro_rules! impl_from_state_for_scheduler {
    ($Context: ident) => {
        impl From<State<$Context>> for Scheduler {
            fn from(state: State<$Context>) -> Self {
                Scheduler::$Context(state)
            }
        }
    };
}

impl_from_state_for_scheduler!(Free);
impl_from_state_for_scheduler!(SettingUp);
impl_from_state_for_scheduler!(PendingReboot);
impl_from_state_for_scheduler!(Ready);
impl_from_state_for_scheduler!(Busy);
impl_from_state_for_scheduler!(Done);

impl<C: Context> From<C> for State<C> {
    fn from(ctx: C) -> Self {
        State { ctx }
    }
}

impl State<Free> {
    pub fn schedule(self, work_set: WorkSet) -> State<SettingUp> {
        let ctx = SettingUp { work_set };
        ctx.into()
    }
}

pub enum SetupDone {
    Ready(State<Ready>),
    PendingReboot(State<PendingReboot>),
}

impl State<SettingUp> {
    pub async fn finish(self, runner: &mut dyn ISetupRunner) -> Result<SetupDone> {
        let work_set = self.ctx.work_set;

        let output = runner.run(&work_set).await?;

        if let Some(output) = output {
            if !output.exit_status.success {
                anyhow::bail!("setup script failed: {:?}", output);
            }
        }

        let done = if work_set.reboot {
            let ctx = PendingReboot { work_set };
            SetupDone::PendingReboot(ctx.into())
        } else {
            let ctx = Ready { work_set };
            SetupDone::Ready(ctx.into())
        };

        Ok(done)
    }
}

impl State<PendingReboot> {
    pub fn reboot_context(self) -> RebootContext {
        RebootContext::new(self.ctx.work_set)
    }
}

impl State<Ready> {
    pub async fn run(self) -> Result<State<Busy>> {
        let mut workers = vec![];

        for work in self.ctx.work_set.work_units {
            let worker = Some(Worker::new(work));
            workers.push(worker);
        }

        let ctx = Busy { workers };
        let state = ctx.into();

        Ok(state)
    }
}

impl State<Busy> {
    pub async fn update(
        mut self,
        events: &mut Vec<WorkerEvent>,
        runner: &mut dyn IWorkerRunner,
    ) -> Result<Updated> {
        for worker_slot in &mut self.ctx.workers {
            let worker = worker_slot.take().unwrap().update(events, runner).await?;

            worker_slot.replace(worker);
        }

        let updated = if self.all_workers_done() {
            Updated::Done(Done.into())
        } else {
            Updated::Busy(self)
        };

        Ok(updated)
    }

    fn all_workers_done(&self) -> bool {
        self.ctx
            .workers
            .iter()
            .all(|worker| worker.as_ref().unwrap().is_done())
    }

    pub fn stop(&mut self, task_id: TaskId) -> Result<()> {
        for worker in &mut self.ctx.workers {
            let worker = worker.as_mut().unwrap();

            if let Worker::Running(state) = worker {
                if state.work().task_id == task_id {
                    state.kill()?;
                }
            }
        }

        Ok(())
    }
}

pub enum Updated {
    Busy(State<Busy>),
    Done(State<Done>),
}

impl From<Updated> for Scheduler {
    fn from(updated: Updated) -> Self {
        match updated {
            Updated::Busy(state) => state.into(),
            Updated::Done(state) => state.into(),
        }
    }
}

impl State<Done> {}

impl From<Option<RebootContext>> for Scheduler {
    fn from(ctx: Option<RebootContext>) -> Self {
        if let Some(ctx) = ctx {
            let work_set = ctx.work_set;
            let ctx = Ready { work_set };
            let state = State { ctx };
            state.into()
        } else {
            let state = State { ctx: Free };
            state.into()
        }
    }
}
