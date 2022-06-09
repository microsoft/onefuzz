// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::time::Duration;

use anyhow::{Context, Result};
use downcast_rs::Downcast;
use onefuzz::az_copy;
use onefuzz::process::Output;
use tokio::fs;
use tokio::process::Command;

use crate::work::*;

// Default to 59 minutes, just under the service's `NODE_EXPIRATION_TIME` of 1 hour.
const DEFAULT_SETUP_SCRIPT_TIMEOUT: Duration = Duration::from_secs(59 * 60);

const SETUP_PATH_ENV: &str = "ONEFUZZ_TARGET_SETUP_PATH";

pub type SetupOutput = Option<Output>;

#[async_trait]
pub trait ISetupRunner: Downcast {
    async fn run(&mut self, work_set: &WorkSet) -> Result<SetupOutput>;
}

impl_downcast!(ISetupRunner);

#[async_trait]
impl ISetupRunner for SetupRunner {
    async fn run(&mut self, work_set: &WorkSet) -> Result<SetupOutput> {
        self.run(work_set).await
    }
}

#[derive(Clone, Copy, Debug)]
pub struct SetupRunner;

impl SetupRunner {
    pub async fn run(&mut self, work_set: &WorkSet) -> Result<SetupOutput> {
        info!("running setup for work set");

        work_set.save_context().await?;

        // Download the setup container.
        let setup_url = work_set.setup_url.url()?;
        let setup_dir = work_set.setup_dir()?;

        // `azcopy sync` requires the local dir to exist.
        fs::create_dir_all(&setup_dir).await.with_context(|| {
            format!("unable to create setup container: {}", setup_dir.display())
        })?;
        az_copy::sync(setup_url.to_string(), &setup_dir, false).await?;

        debug!(
            "synced setup container from {} to {}",
            setup_url,
            setup_dir.display(),
        );

        // Ensure `target_exe` is executable, so tasks don't have to.
        onefuzz::fs::set_executable(&setup_dir).await?;

        // Create setup container directory symlinks for tasks.
        for work_unit in &work_set.work_units {
            create_setup_symlink(&setup_dir, work_unit).await?;
        }

        // Run setup script, if any.
        let setup_script = SetupScript::new(setup_dir).await?;

        let output = if let Some(setup_script) = setup_script {
            info!(
                "running setup script from {}",
                setup_script.path().display()
            );

            let output = setup_script.invoke(None).await?;

            if output.exit_status.success {
                debug!(
                    "setup script succeeded. stdout:{:?} stderr:{:?}",
                    &output.stdout, &output.stderr,
                );
                info!("setup script succeeded");
            } else {
                error!(
                    "setup script failed.  stdout:{:?} stderr:{:?}",
                    &output.stdout, &output.stderr,
                );
            }

            Some(output)
        } else {
            info!("no setup script to run");

            None
        };

        Ok(output)
    }
}

#[cfg(target_family = "windows")]
async fn create_setup_symlink(setup_dir: &Path, work_unit: &WorkUnit) -> Result<()> {
    use std::os::windows::fs::symlink_dir;
    use tokio::task::spawn_blocking;

    let working_dir = work_unit.working_dir()?;

    fs::create_dir(&working_dir).await.with_context(|| {
        format!(
            "unable to create working directory: {}",
            working_dir.display()
        )
    })?;
    let task_setup_dir = working_dir.join("setup");

    // Tokio does not ship async versions of the `std::fs::os` symlink
    // functions (unlike the Unix equivalents).
    let src = setup_dir.to_owned();
    let dst = task_setup_dir.clone();
    let blocking = spawn_blocking(move || symlink_dir(src, dst));
    blocking.await??;

    debug!(
        "created symlink from {} to {}",
        setup_dir.display(),
        task_setup_dir.display(),
    );

    Ok(())
}

#[cfg(target_family = "unix")]
async fn create_setup_symlink(setup_dir: &Path, work_unit: &WorkUnit) -> Result<()> {
    use tokio::fs::symlink;

    let working_dir = work_unit.working_dir()?;

    tokio::fs::create_dir_all(&working_dir)
        .await
        .with_context(|| {
            format!(
                "unable to create working directory: {}",
                working_dir.display()
            )
        })?;

    let task_setup_dir = working_dir.join("setup");
    symlink(&setup_dir, &task_setup_dir)
        .await
        .with_context(|| {
            format!(
                "unable to create symlink from {} to {}",
                setup_dir.display(),
                task_setup_dir.display()
            )
        })?;

    debug!(
        "created symlink from {} to {}",
        setup_dir.display(),
        task_setup_dir.display(),
    );

    Ok(())
}

#[cfg(target_family = "windows")]
const SETUP_SCRIPT: &str = "setup.ps1";

#[cfg(target_family = "unix")]
const SETUP_SCRIPT: &str = "setup.sh";

pub struct SetupScript {
    setup_dir: PathBuf,
    script_path: PathBuf,
}

impl SetupScript {
    pub async fn new(setup_dir: impl AsRef<Path>) -> Result<Option<Self>> {
        let setup_dir = setup_dir.as_ref().to_path_buf();
        let script_path = setup_dir.join(SETUP_SCRIPT);

        let script = if onefuzz::fs::exists(&script_path).await? {
            Some(Self {
                setup_dir,
                script_path,
            })
        } else {
            None
        };

        Ok(script)
    }

    pub fn path(&self) -> &Path {
        &self.script_path
    }

    pub async fn invoke(&self, timeout: impl Into<Option<Duration>>) -> Result<Output> {
        let timeout = timeout.into().unwrap_or(DEFAULT_SETUP_SCRIPT_TIMEOUT);

        let timed = tokio::time::timeout(timeout, self.setup_command().output())
            .await
            .context("setup script timed out")?;
        let output = timed?.into();

        Ok(output)
    }

    #[cfg(target_family = "windows")]
    fn setup_command(&self) -> Command {
        let mut cmd = Command::new("powershell.exe");

        cmd.env(SETUP_PATH_ENV, &self.setup_dir);
        cmd.arg("-ExecutionPolicy");
        cmd.arg("Unrestricted");
        cmd.arg("-File");
        cmd.arg(&self.script_path);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        cmd
    }

    #[cfg(target_family = "unix")]
    fn setup_command(&self) -> Command {
        let mut cmd = Command::new("bash");

        cmd.env(SETUP_PATH_ENV, &self.setup_dir);
        cmd.arg(&self.script_path);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        cmd
    }
}

#[cfg(test)]
pub mod double;
