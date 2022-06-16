use std::{path::{PathBuf, Path}, collections::HashMap, process::{Command, Stdio}, time::Duration};
use std::env;
use onefuzz::{syncdir::SyncedDir, expand::Expand};
use anyhow::{Context, Result};
use storage_queue::{Message, QueueClient};
use timer::Timer;
use tokio::task::spawn_blocking;
use uuid::Uuid;

use crate::tasks::{generic::input_poller::InputPoller, config::CommonConfig, heartbeat::HeartbeatSender};

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

        Ok(())
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

    async fn try_record_input(&self, input: &Path) -> Result<()> {
        let mut cmd = self.command_for_input(input).await?;
        let timeout = self.config.timeout();
        let coverage = spawn_blocking(move || {
            record_coverage(&mut cmd, timeout)
        })
        .await??;

        //TODO: Merge the coverage files
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

        //TODO: ./dotnet-coverage collect --output-format cobertura -o "fullcommand.cobertura.xml" "/usr/bin/dotnet bin/Debug/net6.0/testcoverage.dll input2"
        let dotnet_coverage_path = dotnet_coverage_path()?;
        let dotnet_path = dotnet_path()?;
        let id = Uuid::new_v4();

        let mut cmd = Command::new(dotnet_coverage_path);
        cmd.arg("collect")
            .args(["--output-format", "cobertura"])
            .args(["-o", &id.to_string()])
            .arg(format!("{} {}", self.config.target_exe.to_string_lossy(), input.to_string_lossy()));

        let target_options = expand.evaluate(&self.config.target_options)?;
        cmd.args(target_options);

        for (k, v) in &self.config.target_env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        cmd.env_remove("RUST_LOG");
        cmd.stdin(Stdio::null());
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        Ok(cmd)
    }

    async fn command_for_merge(&self) -> Result<Command> {
        //TODO: dotnet coverage merge output.cobertura.xml output2.cobertura.xml --output-format cobertura -o "final.cobertura.xml"

        Ok(())
    }

    
}

fn record_coverage(cmd: &mut Command, timeout: Duration) -> Result<()> {
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
    let dotnet_coverage = Path::new(&tools_dir)
        .join(dotnet_coverage_exectuable);

    Ok(dotnet_coverage)
}

fn dotnet_path() -> Result<PathBuf> {
    let tools_dir = env::var("ONEFUZZ_TOOLS")?;
    #[cfg(target_os = "windows")]
    let dotnet_exectuable = "dotnet.exe";
    #[cfg(not(target_os = "windows"))]
    let dotnet_exectuable = "dotnet";
    let dotnet = Path::new(&tools_dir)
        .join("dotnet") // The folder containing the dotnet executable
        .join(dotnet_exectuable); // The dotnet executable

    Ok(dotnet)
}
