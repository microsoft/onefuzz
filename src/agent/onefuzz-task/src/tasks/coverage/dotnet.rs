use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{
    expand::{Expand, PlaceHolder},
    syncdir::SyncedDir,
};
use reqwest::Url;
use std::env;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    time::Duration,
};
use storage_queue::{Message, QueueClient};
use timer::Timer;
use tokio::{fs, task::spawn_blocking};
use tokio_stream::wrappers::ReadDirStream;
use uuid::Uuid;

use crate::tasks::{
    config::CommonConfig,
    generic::input_poller::{CallbackImpl, InputPoller, Processor},
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
};

use super::COBERTURA_COVERAGE_FILE;

const MAX_COVERAGE_RECORDING_ATTEMPTS: usize = 2;
const DEFAULT_TARGET_TIMEOUT: Duration = Duration::from_secs(5);

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

impl Config {
    pub fn timeout(&self) -> Duration {
        self.target_timeout
            .map(Duration::from_secs)
            .unwrap_or(DEFAULT_TARGET_TIMEOUT)
    }
}

pub struct CoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("dotnet_coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting dotnet_coverage task");
        self.config.coverage.init_pull().await?;

        let heartbeat = self.config.common.init_heartbeat(None).await?;
        let mut context = TaskContext::new(&self.config, heartbeat);

        if !context.uses_input() {
            bail!("input is not specified on the command line or arguments for the target");
        }

        context.heartbeat.alive();

        let mut seen_inputs = false;

        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.local_path.display());

            dir.init_pull().await?;
            let dir_count = context.record_corpus(&dir.local_path).await?;

            if dir_count > 0 {
                seen_inputs = true;
            }

            info!(
                "recorded coverage for {} inputs from {}",
                dir_count,
                dir.local_path.display()
            );

            context.heartbeat.alive();
        }

        if seen_inputs {
            context.save_and_sync_coverage().await?;
        }

        context.heartbeat.alive();

        if let Some(queue) = &self.config.input_queue {
            info!("polling queue for new coverage inputs");

            let callback = CallbackImpl::new(queue.clone(), context)?;
            self.poller.run(callback).await?;
        }

        Ok(())
    }
}

struct TaskContext<'a> {
    config: &'a Config,
    heartbeat: Option<TaskHeartbeatClient>,
}

impl<'a> TaskContext<'a> {
    pub fn new(config: &'a Config, heartbeat: Option<TaskHeartbeatClient>) -> Self {
        Self { config, heartbeat }
    }
    async fn record_corpus(&mut self, dir: &Path) -> Result<usize> {
        use futures::stream::StreamExt;

        let mut corpus = fs::read_dir(dir)
            .await
            .map(ReadDirStream::new)
            .with_context(|| format!("unable to read corpus directory: {}", dir.display()))?;

        let mut count = 0;

        while let Some(entry) = corpus.next().await {
            match entry {
                Ok(entry) => {
                    if entry.file_type().await?.is_file() {
                        self.record_input(&entry.path()).await?;
                        count += 1;

                        // make sure we save & sync coverage every 10 inputs
                        if count % 10 == 0 {
                            self.save_and_sync_coverage().await?;
                        }
                    } else {
                        warn!("skipping non-file dir entry: {}", entry.path().display());
                    }
                }
                Err(err) => {
                    error!("{:?}", err);
                }
            }
        }

        Ok(count)
    }

    async fn record_input(&mut self, input: &Path) -> Result<()> {
        debug!("recording coverage for {}", input.display());
        let attempts = MAX_COVERAGE_RECORDING_ATTEMPTS;

        for attempt in 1..=attempts {
            let result = self.try_record_input(input).await;

            if let Err(err) = &result {
                // Recording failed, check if we can retry.
                if attempt < attempts {
                    // We will retry, but warn to capture the error if we succeed.
                    warn!(
                        "error recording coverage for input = {}: {:?}",
                        input.display(),
                        err
                    );
                } else {
                    // Final attempt, do not retry.
                    return result.with_context(|| {
                        format_err!(
                            "failed to record coverage for input = {} after {} attempts",
                            input.display(),
                            attempts
                        )
                    });
                }
            } else {
                // We successfully recorded the coverage for `input`, so stop.
                break;
            }
        }

        Ok(())
    }

    async fn save_and_sync_coverage(&self) -> Result<()> {
        info!("Saving and syncing coverage");
        let mut cmd = self.command_for_merge().await?;
        let timeout = self.config.timeout();
        let merge = spawn_blocking(move || spawn_with_timeout(&mut cmd, timeout)).await??;
        info!("Pushing coverage");
        self.config.coverage.sync_push().await?;

        Ok(merge)
    }

    async fn try_record_input(&self, input: &Path) -> Result<()> {
        let mut cmd = self.command_for_input(input).await?;
        let timeout = self.config.timeout();
        let coverage = spawn_blocking(move || spawn_with_timeout(&mut cmd, timeout)).await??;
        Ok(coverage)
    }

    async fn command_for_input(&self, input: &Path) -> Result<Command> {
        let expand = Expand::new()
            .machine_id()
            .await?
            .input_path(input)
            .job_id(&self.config.common.job_id)
            .setup_dir(&self.config.common.setup_dir)
            .target_exe(&self.config.target_exe)
            .target_options(&self.config.target_options)
            .task_id(&self.config.common.task_id);

        let dotnet_coverage_path = dotnet_coverage_path()?;
        let dotnet_path = dotnet_path()?;
        let id = Uuid::new_v4();
        let output_file_path = self
            .intermediate_coverage_files_path()?
            .join(format!("{}.cobertura.xml", id));

        let target_options = expand.evaluate(&self.config.target_options)?;

        let mut cmd = Command::new(dotnet_coverage_path);
        cmd.arg("collect")
            .args(["--output-format", "cobertura"])
            .args(["-o", &output_file_path.to_string_lossy()])
            .arg(format!(
                "{} {} {}",
                dotnet_path.to_string_lossy(),
                self.config.target_exe.canonicalize()?.to_string_lossy(),
                target_options.join(" ")
            ));

        info!("{:?}", &cmd);

        for (k, v) in &self.config.target_env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        cmd.env_remove("RUST_LOG");
        cmd.stdin(Stdio::null());
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        Ok(cmd)
    }

    fn working_dir(&self) -> Result<PathBuf> {
        Ok(self.config.coverage.local_path.canonicalize()?)
    }

    fn intermediate_coverage_files_path(&self) -> Result<PathBuf> {
        Ok(self.working_dir()?
            .join("intermediate-coverage-files"))
    }

    async fn command_for_merge(&self) -> Result<Command> {
        let dotnet_coverage_path = dotnet_coverage_path()?;

        let output_file = self.working_dir()?.join(COBERTURA_COVERAGE_FILE);

        let mut cmd = Command::new(dotnet_coverage_path);
        cmd.arg("merge")
            .args(["--output-format", "cobertura"])
            .args(["-o", &output_file.to_string_lossy()])
            .arg("-r")
            .arg("--remove-input-files")
            .arg("*.cobertura.xml")
            .arg(COBERTURA_COVERAGE_FILE); // This lets us 'fold' any new coverage into the existing coverage file.

        cmd.current_dir(self.working_dir()?);

        info!("{:?}", &cmd);
        info!("From: {:?}", self.working_dir()?);

        cmd.env_remove("RUST_LOG");
        cmd.stdin(Stdio::null());
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());
        
        Ok(cmd)
    }

    fn uses_input(&self) -> bool {
        let input = PlaceHolder::Input.get_string();

        for entry in &self.config.target_options {
            if entry.contains(&input) {
                return true;
            }
        }
        for (k, v) in &self.config.target_env {
            if k == &input || v.contains(&input) {
                return true;
            }
        }

        false
    }
}

fn spawn_with_timeout(cmd: &mut Command, timeout: Duration) -> Result<()> {
    let mut child = cmd.spawn()?;
    let _timer = Timer::new(timeout, move || child.kill());
    Ok(())
}

fn dotnet_coverage_path() -> Result<PathBuf> {
    let tools_dir = env::var("ONEFUZZ_TOOLS")?;
    #[cfg(target_os = "windows")]
    let dotnet_coverage_exectuable = "dotnet-coverage.exe";
    #[cfg(not(target_os = "windows"))]
    let dotnet_coverage_exectuable = "dotnet-coverage";
    let dotnet_coverage = Path::new(&tools_dir).join(dotnet_coverage_exectuable);

    Ok(dotnet_coverage)
}

fn dotnet_path() -> Result<PathBuf> {
    let dotnet_root_dir = env::var("DOTNET_ROOT")?;
    #[cfg(target_os = "windows")]
    let dotnet_exectuable = "dotnet.exe";
    #[cfg(not(target_os = "windows"))]
    let dotnet_exectuable = "dotnet";
    let dotnet = Path::new(&dotnet_root_dir)
        .join(dotnet_exectuable); // The dotnet executable

    Ok(dotnet)
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat.alive();

        self.record_input(input).await?;
        self.save_and_sync_coverage().await?;

        Ok(())
    }
}
