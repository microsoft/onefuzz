// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
};

use anyhow::{Context as AnyhowContext, Result};
use downcast_rs::Downcast;
use onefuzz::process::{ExitStatus, Output};
use tokio::fs;

use crate::work::*;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum WorkerEvent {
    Running {
        task_id: TaskId,
    },
    Done {
        task_id: TaskId,
        exit_status: ExitStatus,
        stderr: String,
        stdout: String,
    },
}

pub enum Worker {
    Ready(State<Ready>),
    Running(State<Running>),
    Done(State<Done>),
}

impl Worker {
    pub fn new(setup_dir: impl AsRef<Path>, work: WorkUnit) -> Self {
        let ctx = Ready {
            setup_dir: PathBuf::from(setup_dir.as_ref()),
        };
        let state = State { ctx, work };
        state.into()
    }

    pub fn is_done(&self) -> bool {
        matches!(self, Worker::Done(..))
    }

    pub async fn update(
        self,
        events: &mut Vec<WorkerEvent>,
        runner: &mut dyn IWorkerRunner,
    ) -> Result<Self> {
        let worker = match self {
            Worker::Ready(state) => {
                let state = state.run(runner).await?;
                let event = WorkerEvent::Running {
                    task_id: state.work.task_id,
                };
                events.push(event);
                state.into()
            }
            Worker::Running(state) => match state.wait()? {
                Waited::Done(state) => {
                    let output = state.output();
                    let event = WorkerEvent::Done {
                        exit_status: output.exit_status,
                        stderr: output.stderr,
                        stdout: output.stdout,
                        task_id: state.work.task_id,
                    };
                    events.push(event);
                    state.into()
                }
                Waited::Running(state) => state.into(),
            },
            Worker::Done(state) => {
                // Nothing to do for workers that are done.
                state.into()
            }
        };

        Ok(worker)
    }
}

pub struct Ready {
    setup_dir: PathBuf,
}

pub struct Running {
    child: Box<dyn IWorkerChild>,
}

pub struct Done {
    output: Output,
}

pub trait Context {}

impl Context for Ready {}
impl Context for Running {}
impl Context for Done {}

pub struct State<C: Context> {
    ctx: C,
    work: WorkUnit,
}

impl<C: Context> State<C> {
    pub fn work(&self) -> &WorkUnit {
        &self.work
    }
}

impl State<Ready> {
    pub async fn run(self, runner: &mut dyn IWorkerRunner) -> Result<State<Running>> {
        let child = runner.run(&self.ctx.setup_dir, &self.work).await?;

        let state = State {
            ctx: Running { child },
            work: self.work,
        };

        Ok(state)
    }
}

impl State<Running> {
    pub fn wait(mut self) -> Result<Waited> {
        let waited = self.ctx.child.try_wait()?;

        if let Some(output) = waited {
            let ctx = Done { output };
            let state = State {
                ctx,
                work: self.work,
            };
            Ok(Waited::Done(state))
        } else {
            Ok(Waited::Running(self))
        }
    }

    pub fn kill(&mut self) -> Result<()> {
        self.ctx.child.kill()
    }
}

pub enum Waited {
    Running(State<Running>),
    Done(State<Done>),
}

impl State<Done> {
    pub fn output(&self) -> Output {
        self.ctx.output.clone()
    }
}

macro_rules! impl_from_state_for_worker {
    ($Context: ident) => {
        impl From<State<$Context>> for Worker {
            fn from(state: State<$Context>) -> Self {
                Worker::$Context(state)
            }
        }
    };
}

impl_from_state_for_worker!(Ready);
impl_from_state_for_worker!(Running);
impl_from_state_for_worker!(Done);

#[async_trait]
pub trait IWorkerRunner: Downcast {
    async fn run(&mut self, setup_dir: &Path, work: &WorkUnit) -> Result<Box<dyn IWorkerChild>>;
}

impl_downcast!(IWorkerRunner);

pub trait IWorkerChild: Downcast {
    fn try_wait(&mut self) -> Result<Option<Output>>;

    fn kill(&mut self) -> Result<()>;
}

impl_downcast!(IWorkerChild);

pub struct WorkerRunner;

#[async_trait]
impl IWorkerRunner for WorkerRunner {
    async fn run(&mut self, setup_dir: &Path, work: &WorkUnit) -> Result<Box<dyn IWorkerChild>> {
        let working_dir = work.working_dir()?;

        debug!("worker working dir = {}", working_dir.display());

        fs::create_dir_all(&working_dir).await.with_context(|| {
            format!(
                "unable to create working directory: {}",
                working_dir.display()
            )
        })?;

        debug!("created worker working dir: {}", working_dir.display());

        let config_path = work.config_path()?;

        fs::write(&config_path, work.config.expose_ref())
            .await
            .with_context(|| format!("unable to save task config: {}", config_path.display()))?;

        debug!(
            "wrote worker config to config_path = {}",
            config_path.display()
        );

        info!(
            "spawning `onefuzz-agent`; cwd = {}, job_id = {}, task_id = {}",
            working_dir.display(),
            work.job_id,
            work.task_id,
        );

        let mut cmd = Command::new("onefuzz-agent");
        cmd.current_dir(&working_dir);
        cmd.arg("managed");
        cmd.arg("config.json");
        cmd.arg(setup_dir);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        let child = cmd.spawn().context("onefuzz-agent failed to start")?;
        let child = Box::new(child);

        Ok(child)
    }
}

impl IWorkerChild for Child {
    fn try_wait(&mut self) -> Result<Option<Output>> {
        let output = if let Some(exit_status) = self.try_wait()? {
            let exit_status = exit_status.into();
            let stderr = read_to_string(&mut self.stderr)?;
            let stdout = read_to_string(&mut self.stdout)?;

            Some(Output {
                exit_status,
                stderr,
                stdout,
            })
        } else {
            None
        };

        Ok(output)
    }

    fn kill(&mut self) -> Result<()> {
        use std::io::ErrorKind;

        let killed = self.kill();

        if let Err(err) = &killed {
            if let ErrorKind::InvalidInput = err.kind() {
                // Child already exited, not an error for us.
                return Ok(());
            }
        }

        Ok(())
    }
}

fn read_to_string(stream: &mut Option<impl std::io::Read>) -> Result<String> {
    let mut data = Vec::new();
    if let Some(stream) = stream {
        stream.read_to_end(&mut data)?;
    }

    Ok(String::from_utf8_lossy(&data).into_owned())
}

#[cfg(test)]
pub mod double;

#[cfg(test)]
mod tests;
