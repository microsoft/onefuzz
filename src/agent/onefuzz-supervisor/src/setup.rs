// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};
use std::process::{Output, Stdio};

use anyhow::Result;
use downcast_rs::Downcast;
use onefuzz::az_copy;
use onefuzz::blob::BlobContainerUrl;
use tokio::fs;
use tokio::process::Command;

use crate::work::*;

const SETUP_PATH_ENV: &str = "ONEFUZZ_TARGET_SETUP_PATH";

#[async_trait]
pub trait ISetupRunner: Downcast {
    async fn run(&mut self, work_set: &WorkSet) -> Result<bool>;
}

impl_downcast!(ISetupRunner);

#[async_trait]
impl ISetupRunner for SetupRunner {
    async fn run(&mut self, work_set: &WorkSet) -> Result<bool> {
        self.run(work_set).await
    }
}

#[derive(Clone, Copy, Debug)]
pub struct SetupRunner;

impl SetupRunner {
    pub async fn run(&mut self, work_set: &WorkSet) -> Result<bool> {
        info!("running setup for work set");

        // Download the setup container.
        let setup_url = work_set.setup_url.clone();
        let setup_url = BlobContainerUrl::new(setup_url)?;

        let setup_dir = setup_url.container();
        let setup_dir = onefuzz::fs::onefuzz_root()?
            .join("blob-containers")
            .join(setup_dir);

        // `azcopy sync` requires the local dir to exist.
        fs::create_dir_all(&setup_dir).await?;
        az_copy::sync(work_set.setup_url.to_string(), &setup_dir).await?;

        verbose!(
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

        if let Some(setup_script) = setup_script {
            info!(
                "running setup script from {}",
                setup_script.path().display()
            );

            let output = setup_script.invoke().await?;

            info!(
                "setup script run; status = {}, path = {}",
                output.status,
                setup_script.path().display(),
            );

            if output.status.success() {
                verbose!(
                    "setup script succeeded:\nstdout = {}\nstderr = {}",
                    String::from_utf8_lossy(&output.stdout),
                    String::from_utf8_lossy(&output.stderr),
                );
                info!("setup script succeeded");
            } else {
                error!(
                    "setup script failed:\nstdout = {}\nstderr = {}",
                    String::from_utf8_lossy(&output.stdout),
                    String::from_utf8_lossy(&output.stderr),
                );
                anyhow::bail!("setup script failed:\nstdout = {}\nstderr = {}");
            }
        } else {
            info!("no setup script to run");
        }

        Ok(work_set.reboot)
    }
}

#[cfg(target_os = "windows")]
async fn create_setup_symlink(setup_dir: &Path, work_unit: &WorkUnit) -> Result<()> {
    use std::os::windows::fs::symlink_dir;
    use tokio::task::spawn_blocking;

    fs::create_dir(&work_unit.working_dir()?).await?;
    let task_setup_dir = work_unit.working_dir()?.join("setup");

    // Tokio does not ship async versions of the `std::fs::os` symlink
    // functions (unlike the Unix equivalents).
    let src = setup_dir.to_owned();
    let dst = task_setup_dir.clone();
    let blocking = spawn_blocking(move || symlink_dir(src, dst));
    blocking.await??;

    verbose!(
        "created symlink from {} to {}",
        setup_dir.display(),
        task_setup_dir.display(),
    );

    Ok(())
}

#[cfg(target_os = "linux")]
async fn create_setup_symlink(setup_dir: &Path, work_unit: &WorkUnit) -> Result<()> {
    use tokio::fs::os::unix::symlink;

    tokio::fs::create_dir_all(work_unit.working_dir()?).await?;

    let task_setup_dir = work_unit.working_dir()?.join("setup");
    symlink(&setup_dir, &task_setup_dir).await?;

    verbose!(
        "created symlink from {} to {}",
        setup_dir.display(),
        task_setup_dir.display(),
    );

    Ok(())
}

#[cfg(target_os = "windows")]
const SETUP_SCRIPT: &str = "setup.ps1";

#[cfg(target_os = "linux")]
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

    pub async fn invoke(&self) -> Result<Output> {
        Ok(self.setup_command().output().await?)
    }

    #[cfg(target_os = "windows")]
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

    #[cfg(target_os = "linux")]
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
