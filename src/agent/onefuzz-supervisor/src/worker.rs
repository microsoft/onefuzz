// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
use std::{
    path::{Path, PathBuf},
    process::{Child, ChildStderr, ChildStdout, Command, Stdio},
    thread::{self, JoinHandle},
};

use anyhow::{format_err, Context as AnyhowContext, Result};
use downcast_rs::Downcast;
use onefuzz::process::{ExitStatus, Output};
use tokio::fs;

use crate::buffer::TailBuffer;
use crate::work::*;

// Max length of captured output streams from worker child processes.
const MAX_TAIL_LEN: usize = 4096;

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

        Ok(Box::new(RedirectedChild::spawn(cmd)?))
    }
}

trait SuspendableChild {
    fn suspend(&mut self) -> Result<()>;
}

#[cfg(target_os = "windows")]
impl SuspendableChild for Child {
    fn suspend(&mut self) -> Result<()> {
        let result = unsafe { winapi::um::debugapi::DebugActiveProcess(self.id() as u32) };
        if result == 0 {
            bail!("unable to suspend child process");
        }
        Ok(())
    }
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
impl SuspendableChild for Child {
    fn suspend(&mut self) -> Result<()> {
        use nix::sys::signal;
        signal::kill(
            nix::unistd::Pid::from_raw(self.id() as _),
            signal::Signal::SIGSTOP,
        )?;
        Ok(())
    }
}

/// Child process with redirected output streams, tailed by two worker threads.
struct RedirectedChild {
    /// The child process.
    child: Child,

    /// Worker threads which continuously read from the redirected streams.
    streams: Option<StreamReaderThreads>,
}

impl RedirectedChild {
    pub fn spawn(mut cmd: Command) -> Result<Self> {
        // Make sure we capture the child's output streams.
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        let mut child = cmd.spawn().context("onefuzz-agent failed to start")?;

        // Guaranteed by the above.
        let stderr = child.stderr.take().unwrap();
        let stdout = child.stdout.take().unwrap();
        let streams = Some(StreamReaderThreads::new(stderr, stdout));

        Ok(Self { child, streams })
    }
}

/// Worker threads that tail the redirected output streams of a running child process.
struct StreamReaderThreads {
    stderr: JoinHandle<TailBuffer>,
    stdout: JoinHandle<TailBuffer>,
}

struct CapturedStreams {
    stderr: String,
    stdout: String,
}

impl StreamReaderThreads {
    pub fn new(mut stderr: ChildStderr, mut stdout: ChildStdout) -> Self {
        use std::io::Read;

        let stderr = thread::spawn(move || {
            let mut buf = TailBuffer::new(MAX_TAIL_LEN);
            let mut tmp = [0u8; MAX_TAIL_LEN];

            while let Ok(count) = stderr.read(&mut tmp) {
                if count == 0 {
                    break;
                }
                if let Err(err) = std::io::copy(&mut &tmp[..count], &mut buf) {
                    log::error!("error copying to circular buffer: {}", err);
                    break;
                }
            }

            buf
        });

        let stdout = thread::spawn(move || {
            let mut buf = TailBuffer::new(MAX_TAIL_LEN);
            let mut tmp = [0u8; MAX_TAIL_LEN];

            while let Ok(count) = stdout.read(&mut tmp) {
                if count == 0 {
                    break;
                }

                if let Err(err) = std::io::copy(&mut &tmp[..count], &mut buf) {
                    log::error!("error copying to circular buffer: {}", err);
                    break;
                }
            }

            buf
        });

        Self { stderr, stdout }
    }

    pub fn join(self) -> Result<CapturedStreams> {
        let stderr = self
            .stderr
            .join()
            .map_err(|_| format_err!("stderr tail thread panicked"))?
            .to_string_lossy();
        let stdout = self
            .stdout
            .join()
            .map_err(|_| format_err!("stdout tail thread panicked"))?
            .to_string_lossy();

        Ok(CapturedStreams { stderr, stdout })
    }
}

impl IWorkerChild for RedirectedChild {
    fn try_wait(&mut self) -> Result<Option<Output>> {
        let output = if let Some(exit_status) = self.child.try_wait()? {
            let exit_status = exit_status.into();
            let streams = self.streams.take();
            let streams = streams
                .ok_or_else(|| format_err!("onefuzz-agent streams not captured"))?
                .join()?;

            Some(Output {
                exit_status,
                stderr: streams.stderr,
                stdout: streams.stdout,
            })
        } else {
            None
        };

        Ok(output)
    }

    fn kill(&mut self) -> Result<()> {
        use std::io::ErrorKind;

        // trying to gracefully kill the child process.
        // we ignore the error if it's because the process will be killed anyway
        if let Err(suspend_error) = self.child.suspend() {
            log::info!("error while suspending process: {}", suspend_error);
        }

        let killed = self.child.kill();

        if let Err(err) = &killed {
            if let ErrorKind::InvalidInput = err.kind() {
                // Child already exited, not an error for us.
                return Ok(());
            }
        }

        Ok(())
    }
}

#[cfg(test)]
pub mod double;

#[cfg(test)]
mod tests;
