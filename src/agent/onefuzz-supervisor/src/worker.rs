// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::process::{Child, Command, Stdio};

use anyhow::Result;
use downcast_rs::Downcast;
use tokio::fs;

use crate::work::*;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case", untagged)]
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

/// Serializable representation of a worker process output.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct Output {
    pub exit_status: ExitStatus,
    pub stderr: String,
    pub stdout: String,
}

impl From<std::process::Output> for Output {
    fn from(output: std::process::Output) -> Self {
        let exit_status = output.status.into();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();

        Self { exit_status, stderr, stdout }
    }
}

/// Serializable representation of a worker exit status.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ExitStatus {
    pub code: Option<i32>,
    pub signal: Option<i32>,
    pub success: bool,
}

impl From<std::process::ExitStatus> for ExitStatus {
    #[cfg(target_os = "windows")]
    fn from(status: std::process::ExitStatus) -> Self {
        Self {
            code: status.code(),
            signal: None,
            success: status.success(),
        }
    }

    #[cfg(target_os = "linux")]
    fn from(status: std::process::ExitStatus) -> Self {
        use std::os::unix::process::ExitStatusExt;

        Self {
            code: status.code(),
            signal: status.signal(),
            success: status.success(),
        }
    }
}

pub enum Worker {
    Ready(State<Ready>),
    Running(State<Running>),
    Done(State<Done>),
}

impl Worker {
    pub fn new(work: WorkUnit) -> Self {
        let ctx = Ready;
        let state = State { ctx, work };
        state.into()
    }

    pub fn is_done(&self) -> bool {
        if let Worker::Done(..) = self {
            true
        } else {
            false
        }
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

pub struct Ready;

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
        let child = runner.run(&self.work).await?;

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
    async fn run(&mut self, work: &WorkUnit) -> Result<Box<dyn IWorkerChild>>;
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
    async fn run(&mut self, work: &WorkUnit) -> Result<Box<dyn IWorkerChild>> {
        let working_dir = work.working_dir()?;

        verbose!("worker working dir = {}", working_dir.display());

        fs::create_dir_all(&working_dir).await?;

        verbose!("created worker working dir: {}", working_dir.display());

        let config_path = work.config_path()?;

        fs::write(&config_path, &work.config).await?;

        verbose!(
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
        cmd.arg("-c");
        cmd.arg("config.json");
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        let child = cmd.spawn()?;
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
