// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::{Child, ChildStderr, ChildStdout, Command, Stdio},
    thread::{self, JoinHandle},
    time::Duration,
};

use anyhow::{format_err, Context as AnyhowContext, Result};
use downcast_rs::Downcast;
use ipc_channel::ipc::{IpcOneShotServer, IpcReceiver, IpcSender};
use onefuzz::{
    ipc::IpcMessageKind,
    machine_id::MachineIdentity,
    process::{ExitStatus, Output},
};
use tokio::{
    fs, task,
    time::{error::Elapsed, timeout},
};
use url::Url;
use uuid::Uuid;

use crate::work::*;
use crate::{buffer::TailBuffer, log_uploader::Uploader};

use serde_json::Value;

// Max length of captured output streams from worker child processes.
const MAX_TAIL_LEN: usize = 40960;

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

#[derive(Debug)]
pub enum Worker {
    Ready(State<Ready>),
    Running(State<Running>),
    Stopping(State<Stopping>),
    Done(State<Done>),
}

impl Worker {
    pub fn new(
        work_dir: PathBuf,
        setup_dir: PathBuf,
        extra_setup_dir: Option<PathBuf>,
        work: WorkUnit,
    ) -> Self {
        let ctx = Ready {
            work_dir,
            setup_dir,
            extra_setup_dir,
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
            Worker::Running(state) => match state.wait().await? {
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
            Worker::Stopping(state) => {
                let state = state.kill().await?;
                state.into()
            }
            Worker::Done(state) => {
                // Nothing to do for workers that are done.
                state.into()
            }
        };

        Ok(worker)
    }
}

#[derive(Debug)]
pub struct Ready {
    work_dir: PathBuf,
    setup_dir: PathBuf,
    extra_setup_dir: Option<PathBuf>,
}

#[derive(Debug)]
pub struct Running {
    child: Box<dyn IWorkerChild>,
    _from_agent_to_task: IpcSender<IpcMessageKind>,
    from_task_to_agent: IpcReceiver<IpcMessageKind>,
    log_uploader: Option<Uploader>,
}

#[derive(Debug)]
pub struct Stopping {
    child: Box<dyn IWorkerChild>,
}

#[derive(Debug)]
pub struct Done {
    output: Output,
}

pub trait Context {}

impl Context for Ready {}
impl Context for Running {}
impl Context for Stopping {}
impl Context for Done {}

#[derive(Debug)]
pub struct State<C: Context> {
    ctx: C,
    work: WorkUnit,
}

impl<C: Context> State<C> {
    pub fn work(&self) -> &WorkUnit {
        &self.work
    }
}

#[derive(Debug, Deserialize)]
struct LogConfig {
    pub logs: Option<Url>,
    pub task_id: Uuid,
    pub instance_id: Uuid,
}

impl State<Ready> {
    pub async fn run(self, runner: &mut dyn IWorkerRunner) -> Result<State<Running>> {
        // Create and pass the server here
        let (from_agent_to_task_server, from_agent_to_task_endpoint) = IpcOneShotServer::new()?;
        let (from_task_to_agent_server, from_task_to_agent_endpoint) = IpcOneShotServer::new()?;
        let mut child = runner
            .run(
                &self.ctx.setup_dir,
                self.ctx.extra_setup_dir,
                &self.work,
                from_agent_to_task_endpoint,
                from_task_to_agent_endpoint,
            )
            .await?;

        // Accept is a blocking call:
        //      * Accept calls OsIpcOneShotServer::accept - https://doc.servo.org/src/ipc_channel/ipc.rs.html#722-737
        //      * OsIpcOneShotServer::accept calls OsIpcReceiver::recv - https://doc.servo.org/src/ipc_channel/ipc.rs.html#722-737
        //      * OsIpcReceiver::recv is a blocking call - https://doc.servo.org/src/ipc_channel/platform/unix/mod.rs.html#130-133
        // This issue is tracking a non-blocking accept - https://github.com/servo/ipc-channel/issues/307

        info!("waiting for client_sender_server.accept()");

        let (_, from_agent_to_task): (_, IpcSender<IpcMessageKind>) = match timeout(
            Duration::from_secs(30),
            task::spawn_blocking(move || from_agent_to_task_server.accept()),
        )
        .await
        {
            Err(e) => {
                let _: Elapsed = e; // error here is always Elapsed and has no further info

                // see if child exited with any useful information:
                let child_output = match child.try_wait() {
                    Ok(None) => "still running".to_string(),
                    Ok(Some(output)) => {
                        format!("{:?}", output)
                    }
                    Err(e) => format!("{}", e),
                };

                error!(
                    "timeout waiting for client_sender_server.accept(): child status: {}",
                    child_output,
                );

                return Err(format_err!(
                    "timeout waiting for client_sender_server.accept()"
                ));
            }
            Ok(res) => res??,
        };

        info!("waiting for server_receiver_server.accept()");

        let (_, from_task_to_agent): (_, IpcReceiver<IpcMessageKind>) = match timeout(
            Duration::from_secs(30),
            task::spawn_blocking(move || from_task_to_agent_server.accept()),
        )
        .await
        {
            Err(e) => {
                let _: Elapsed = e; // error here is always Elapsed and has no further info

                // see if child exited with any useful information:
                let child_output = match child.try_wait() {
                    Ok(None) => "still running".to_string(),
                    Ok(Some(output)) => {
                        format!("{:?}", output)
                    }
                    Err(e) => format!("{}", e),
                };

                error!(
                    "timeout waiting for server_receiver_server.accept(): child status: {}",
                    child_output
                );

                return Err(format_err!(
                    "timeout waiting for server_receiver_server.accept()",
                ));
            }
            Ok(res) => res??,
        };

        info!("IPC connection bootstrapped");

        let log_path = Path::join(&self.ctx.work_dir, "task_log.txt");

        let work_config = self.work.config.expose_ref();
        let task_config: LogConfig = serde_json::from_str(work_config.as_str())?;

        let log_blob_name = format!(
            "{task_id}/{instance_id}.log",
            task_id = task_config.task_id,
            instance_id = task_config.instance_id
        );

        let log_uploader = task_config.logs.map(|log_url| {
            let log_path = log_path.clone();
            Uploader::start_sync(
                reqwest::Url::parse(log_url.as_str()).unwrap(),
                log_path,
                &log_blob_name,
            )
        });

        let state = State {
            ctx: Running {
                child,
                _from_agent_to_task: from_agent_to_task,
                from_task_to_agent,
                log_uploader,
            },
            work: self.work,
        };

        Ok(state)
    }
}

impl State<Running> {
    pub async fn wait(mut self) -> Result<Waited> {
        while let Ok(res) = self.ctx.from_task_to_agent.try_recv() {
            info!("received message from server_receiver: {:?}", res);
        }

        let waited = self.ctx.child.try_wait()?;

        if let Some(output) = waited {
            let ctx = Done { output };
            let state = State {
                ctx,
                work: self.work,
            };

            // wait for the log uploader to finish
            if let Some(log_uploader) = &self.ctx.log_uploader {
                let _ = log_uploader.stop_sync().await;
            }

            Ok(Waited::Done(state))
        } else {
            Ok(Waited::Running(self))
        }
    }

    pub fn stop(mut self) -> State<Stopping> {
        let c = std::mem::replace(&mut self.ctx.child, Box::new(NoopChild {}));

        State {
            ctx: Stopping { child: c },
            work: self.work,
        }
    }
}

impl Drop for Running {
    fn drop(&mut self) {
        // Drain the channel
        while let Ok(res) = self.from_task_to_agent.try_recv() {
            info!("received message from server_receiver: {:?}", res);
        }
    }
}

impl State<Stopping> {
    pub async fn kill(mut self) -> Result<State<Done>> {
        match timeout(Duration::from_secs(90), async {
            loop {
                match self.ctx.child.try_wait() {
                    Ok(Some(output)) => {
                        return Ok(output);
                    }
                    Ok(None) => { /* Still waiting */ }
                    Err(e) => {
                        error!("failed to wait for graceful shutdown: {:?}", e);
                        return Err(e);
                    }
                };
                info!("Agent didn't respond yet, trying again in 1 second...");
                tokio::time::sleep(Duration::from_secs(1)).await;
            }
        })
        .await
        {
            Ok(Ok(output)) => {
                let ctx = Done { output };
                Ok(State {
                    ctx,
                    work: self.work,
                })
            }
            Err(e) => {
                error!("Time out while shutting down task: {:?}", e);
                error!("Forcefully killing task");
                self.ctx.child.kill()?;
                Err(e.into())
            }
            Ok(Err(e)) => {
                error!("Error shutting down task: {:?}", e);
                error!("Forcefully killing task");
                self.ctx.child.kill()?;
                Err(e)
            }
        }
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
impl_from_state_for_worker!(Stopping);
impl_from_state_for_worker!(Done);

#[async_trait]
pub trait IWorkerRunner: Downcast {
    async fn run(
        &self,
        setup_dir: &Path,
        extra_setup_dir: Option<PathBuf>,
        work: &WorkUnit,
        from_agent_to_task_endpoint: String,
        from_task_to_agent_endpoint: String,
    ) -> Result<Box<dyn IWorkerChild>>;
}

impl_downcast!(IWorkerRunner);

pub trait IWorkerChild: Downcast + std::fmt::Debug {
    fn try_wait(&mut self) -> Result<Option<Output>>;

    fn kill(&mut self) -> Result<()>;
}

impl_downcast!(IWorkerChild);

pub struct WorkerRunner {
    machine_identity: MachineIdentity,
}

impl WorkerRunner {
    pub fn new(machine_identity: MachineIdentity) -> Self {
        Self { machine_identity }
    }
}

#[async_trait]
impl IWorkerRunner for WorkerRunner {
    async fn run(
        &self,
        setup_dir: &Path,
        extra_setup_dir: Option<PathBuf>,
        work: &WorkUnit,
        from_agent_to_task_endpoint: String,
        from_task_to_agent_endpoint: String,
    ) -> Result<Box<dyn IWorkerChild>> {
        let working_dir = work.working_dir(self.machine_identity.machine_id)?;

        debug!("worker working dir = {}", working_dir.display());

        fs::create_dir_all(&working_dir).await.with_context(|| {
            format!(
                "unable to create working directory: {}",
                working_dir.display()
            )
        })?;

        debug!("created worker working dir: {}", working_dir.display());

        // inject the machine_identity in the config file
        let work_config = work.config.expose_ref();
        let mut config: HashMap<&str, Value> = serde_json::from_str(work_config.as_str())?;

        config.insert(
            "machine_identity",
            serde_json::to_value(&self.machine_identity)?,
        );

        config.insert(
            "from_agent_to_task_endpoint",
            from_agent_to_task_endpoint.into(),
        );

        config.insert(
            "from_task_to_agent_endpoint",
            from_task_to_agent_endpoint.into(),
        );

        let config_path = work.config_path(self.machine_identity.machine_id)?;

        fs::write(&config_path, serde_json::to_string(&config)?.as_bytes())
            .await
            .with_context(|| format!("unable to save task config: {}", config_path.display()))?;

        debug!(
            "wrote worker config to config_path = {}",
            config_path.display()
        );

        info!(
            "spawning `onefuzz-task`; cwd = {}, job_id = {}, task_id = {}",
            working_dir.display(),
            work.job_id,
            work.task_id,
        );

        let mut cmd = Command::new("onefuzz-task");
        cmd.current_dir(&working_dir);

        for (k, v) in &work.env {
            cmd.env(k, v);
        }

        cmd.arg("managed");
        cmd.arg(config_path);
        cmd.arg(setup_dir);

        if let Some(extra_setup_dir) = extra_setup_dir {
            cmd.arg(extra_setup_dir);
        }

        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        Ok(Box::new(RedirectedChild::spawn(cmd)?))
    }
}

trait SuspendableChild {
    fn suspend(&self) -> Result<()>;
}

#[cfg(target_os = "windows")]
impl SuspendableChild for Child {
    fn suspend(&self) -> Result<()> {
        // DebugActiveProcess suspends all threads in the process.
        // https://docs.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-debugactiveprocess#remarks
        let result = unsafe { winapi::um::debugapi::DebugActiveProcess(self.id()) };
        if result == 0 {
            bail!("unable to suspend child process");
        }
        Ok(())
    }
}

#[cfg(target_os = "linux")]
impl SuspendableChild for Child {
    fn suspend(&self) -> Result<()> {
        use nix::sys::signal;
        signal::kill(
            nix::unistd::Pid::from_raw(self.id() as _),
            signal::Signal::SIGSTOP,
        )?;
        Ok(())
    }
}

/// Child process with redirected output streams, tailed by two worker threads.
#[derive(Debug)]
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

        let mut child = cmd.spawn().context("onefuzz-task failed to start")?;

        // Guaranteed by the above.
        let stderr = child.stderr.take().unwrap();
        let stdout = child.stdout.take().unwrap();
        let streams = Some(StreamReaderThreads::new(stderr, stdout));

        Ok(Self { child, streams })
    }
}

#[derive(Debug)]
struct NoopChild {}

impl IWorkerChild for NoopChild {
    fn try_wait(&mut self) -> Result<Option<Output>> {
        Ok(None)
    }

    fn kill(&mut self) -> Result<()> {
        Ok(())
    }
}

/// Worker threads that tail the redirected output streams of a running child process.
#[derive(Debug)]
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
                .ok_or_else(|| format_err!("onefuzz-task streams not captured"))?
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

        // Try to gracefully kill the child process to avoid spurious error telemetry;
        // we ignore the error here because the process will be killed anyway
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
