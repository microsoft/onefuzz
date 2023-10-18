// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{
    expand::{Expand, PlaceHolder},
    monitor::DirectoryMonitor,
    syncdir::SyncedDir,
};
use reqwest::Url;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
use std::{env, process::ExitStatus};
use storage_queue::{Message, QueueClient};
use tokio::{fs, process::Command, time::timeout};
use tokio_stream::wrappers::ReadDirStream;
use uuid::Uuid;

use crate::tasks::{
    config::CommonConfig,
    coverage::COBERTURA_COVERAGE_FILE,
    generic::input_poller::{CallbackImpl, InputPoller, Processor},
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::try_resolve_setup_relative_path,
};

const MAX_COVERAGE_RECORDING_ATTEMPTS: usize = 2;
const DEFAULT_TARGET_TIMEOUT: Duration = Duration::from_secs(120);

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,
    pub tools: SyncedDir,

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

impl Config {
    pub fn get_expand(&self) -> Expand<'_> {
        self.common
            .get_expand()
            .target_options(&self.target_options)
            .coverage_dir(&self.coverage.local_path)
            .tools_dir(self.tools.local_path.to_string_lossy().into_owned())
    }
}

pub struct DotnetCoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl DotnetCoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("dotnet_coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting dotnet_coverage task");

        self.config.tools.init_pull().await?;
        self.config.coverage.init_pull().await?;

        let dotnet_path = dotnet_path()?;
        let dotnet_coverage_path = dotnet_coverage_path()?;

        let heartbeat = self.config.common.init_heartbeat(None).await?;
        let mut context = TaskContext::new(
            &self.config,
            heartbeat,
            dotnet_path,
            dotnet_coverage_path.clone(),
        );

        if !context.uses_input() {
            bail!("input is not specified on the command line or arguments for the target");
        }

        context.heartbeat.alive();

        let coverage_local_path = self.config.coverage.local_path.canonicalize()?;
        let intermediate_files_path = intermediate_coverage_files_path(&coverage_local_path)?;
        fs::create_dir_all(&intermediate_files_path).await?;
        let timeout = self.config.timeout();
        let coverage_dir = self.config.coverage.clone();
        let dotnet_coverage_path = dotnet_coverage_path;

        tokio::spawn(async move {
            if let Err(e) = start_directory_monitor(
                &intermediate_files_path,
                &coverage_local_path,
                timeout,
                coverage_dir,
                &dotnet_coverage_path,
            )
            .await
            {
                error!("Directory monitor failed: {}", e);
            }
        });

        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.local_path.display());

            dir.init_pull().await?;
            let dir_count = context.record_corpus(&dir.local_path).await?;

            info!(
                "recorded coverage for {} inputs from {}",
                dir_count,
                dir.local_path.display()
            );

            context.heartbeat.alive();
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

async fn start_directory_monitor(
    intermediate_files_path: &PathBuf,
    coverage_local_path: &Path,
    timeout: Duration,
    coverage_dir: SyncedDir,
    dotnet_coverage_path: &Path,
) -> Result<()> {
    info!(
        "Starting dotnet coverage intermediate file directory monitor on {}",
        intermediate_files_path.to_string_lossy()
    );
    let mut monitor = DirectoryMonitor::new(intermediate_files_path).await?;
    debug!("Started directory monitor, waiting for files");
    while (monitor.next_file().await?).is_some() {
        debug!("Found intermediate coverage file");
        save_and_sync_coverage(
            coverage_local_path,
            timeout,
            coverage_dir.clone(),
            dotnet_coverage_path,
        )
        .await?;
        info!("Updated and synced coverage");
    }
    info!("Shut down directory monitor");

    Ok(())
}

struct TaskContext<'a> {
    config: &'a Config,
    heartbeat: Option<TaskHeartbeatClient>,
    dotnet_path: PathBuf,
    dotnet_coverage_path: PathBuf,
}

impl<'a> TaskContext<'a> {
    pub fn new(
        config: &'a Config,
        heartbeat: Option<TaskHeartbeatClient>,
        dotnet_path: PathBuf,
        dotnet_coverage_path: PathBuf,
    ) -> Self {
        Self {
            config,
            heartbeat,
            dotnet_path,
            dotnet_coverage_path,
        }
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

    async fn try_record_input(&self, input: &Path) -> Result<()> {
        let mut cmd = self.command_for_input(input).await?;
        let timeout = self.config.timeout();
        spawn_with_timeout(&mut cmd, timeout).await?;
        Ok(())
    }

    async fn target_exe(&self) -> Result<String> {
        let tools_dir = self.config.tools.local_path.to_string_lossy().into_owned();

        // Try to expand `target_exe` with support for `{tools_dir}`.
        //
        // Allows using `LibFuzzerDotnetLoader.exe` from a shared tools container.
        let expand = Expand::new(&self.config.common.machine_identity).tools_dir(tools_dir);
        let expanded = expand.evaluate_value(self.config.target_exe.to_string_lossy())?;
        let expanded_path = Path::new(&expanded);

        // Check if `target_exe` was resolved to an absolute path and an existing file.
        // If so, then the user specified a `target_exe` under the `tools` dir.
        let is_absolute = expanded_path.is_absolute();
        let file_exists = fs::metadata(&expanded).await.is_ok();

        if is_absolute && file_exists {
            // We have found `target_exe`, so skip `setup`-relative expansion.
            return Ok(expanded);
        }

        // We haven't yet resolved a local path for `target_exe`. Try the usual
        // `setup`-relative interpretation of the configured value of `target_exe`.
        let resolved = try_resolve_setup_relative_path(&self.config.common.setup_dir, expanded)
            .await?
            .to_string_lossy()
            .into_owned();

        Ok(resolved)
    }

    async fn command_for_input(&self, input: &Path) -> Result<Command> {
        let target_exe = self.target_exe().await?;

        let expand = self
            .config
            .get_expand()
            .input_path(input)
            .target_exe(&target_exe);

        let dotnet_coverage_path = &self.dotnet_coverage_path;
        let dotnet_path = &self.dotnet_path;
        let id = Uuid::new_v4();
        let output_file_path =
            intermediate_coverage_files_path(self.config.coverage.local_path.as_path())?
                .join(format!("{id}.cobertura.xml"));

        let target_options = expand.evaluate(&self.config.target_options)?;

        let mut cmd = Command::new(dotnet_coverage_path);
        cmd.arg("collect")
            .args(["--output-format", "cobertura"])
            .args(["-o", &output_file_path.to_string_lossy()])
            .arg(format!(
                "{} {} -- {}",
                dotnet_path.to_string_lossy(),
                &target_exe,
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

    fn uses_input(&self) -> bool {
        let input = PlaceHolder::Input.get_string();

        for entry in &self.config.target_options {
            if entry.contains(input) {
                return true;
            }
        }
        for (k, v) in &self.config.target_env {
            if k == input || v.contains(input) {
                return true;
            }
        }

        false
    }
}

pub async fn save_and_sync_coverage(
    coverage_local_path: &Path,
    timeout: Duration,
    coverage_dir: SyncedDir,
    dotnet_coverage_path: &Path,
) -> Result<()> {
    info!("Saving and syncing coverage");
    let mut cmd = command_for_merge(coverage_local_path, dotnet_coverage_path).await?;
    spawn_with_timeout(&mut cmd, timeout).await?;
    info!("Pushing coverage");
    coverage_dir.sync_push().await?;

    Ok(())
}

async fn command_for_merge(
    coverage_local_path: &Path,
    dotnet_coverage_path: &Path,
) -> Result<Command> {
    let output_file = working_dir(coverage_local_path)?.join(COBERTURA_COVERAGE_FILE);

    let mut cmd = Command::new(dotnet_coverage_path);
    cmd.arg("merge")
        .args(["--output-format", "cobertura"])
        .args(["-o", &output_file.to_string_lossy()])
        .arg("-r")
        .arg("--remove-input-files")
        .arg("*.cobertura.xml")
        .arg(COBERTURA_COVERAGE_FILE); // This lets us 'fold' any new coverage into the existing coverage file.

    cmd.current_dir(working_dir(coverage_local_path)?);

    info!("{:?}", &cmd);
    info!("From: {:?}", working_dir(coverage_local_path)?);

    cmd.env_remove("RUST_LOG");
    cmd.stdin(Stdio::null());
    cmd.stdout(Stdio::piped());
    cmd.stderr(Stdio::piped());

    Ok(cmd)
}

fn working_dir(coverage_local_path: &Path) -> Result<PathBuf> {
    Ok(coverage_local_path.canonicalize()?)
}

fn intermediate_coverage_files_path(coverage_local_path: &Path) -> Result<PathBuf> {
    Ok(working_dir(coverage_local_path)?.join("intermediate-coverage-files"))
}

async fn spawn_with_timeout(
    cmd: &mut Command,
    timeout_after: Duration,
) -> Result<ExitStatus, std::io::Error> {
    cmd.kill_on_drop(true);
    timeout(timeout_after, cmd.spawn()?.wait()).await?
}

fn dotnet_coverage_path() -> Result<PathBuf> {
    let tools_dir = env::var("ONEFUZZ_TOOLS")?;
    #[cfg(target_os = "windows")]
    let dotnet_coverage_executable = "dotnet-coverage.exe";
    #[cfg(not(target_os = "windows"))]
    let dotnet_coverage_executable = "dotnet-coverage";
    let dotnet_coverage = Path::new(&tools_dir).join(dotnet_coverage_executable);

    Ok(dotnet_coverage)
}

fn dotnet_path() -> Result<PathBuf> {
    let dotnet_root_dir = env::var("DOTNET_ROOT")?;
    #[cfg(target_os = "windows")]
    let dotnet_executable = "dotnet.exe";
    #[cfg(not(target_os = "windows"))]
    let dotnet_executable = "dotnet";
    let dotnet = Path::new(&dotnet_root_dir).join(dotnet_executable); // The dotnet executable

    Ok(dotnet)
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat.alive();

        self.record_input(input).await?;
        let coverage_local_path = self.config.coverage.local_path.canonicalize()?;

        save_and_sync_coverage(
            coverage_local_path.as_path(),
            self.config.timeout(),
            self.config.coverage.clone(),
            &self.dotnet_coverage_path,
        )
        .await?;

        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use onefuzz::expand::PlaceHolder;
    use proptest::prelude::*;

    use crate::config_test_utils::GetExpandFields;

    use super::Config;

    impl GetExpandFields for Config {
        fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)> {
            let mut params = self.common.get_expand_fields();
            params.push((
                PlaceHolder::TargetExe,
                dunce::canonicalize(&self.target_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((PlaceHolder::TargetOptions, self.target_options.join(" ")));
            params.push((
                PlaceHolder::CoverageDir,
                dunce::canonicalize(&self.coverage.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::ToolsDir,
                dunce::canonicalize(&self.tools.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));

            params
        }
    }

    config_test!(Config);
}
