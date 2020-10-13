// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::config::StaticConfig;
use anyhow::Result;
use onefuzz::az_copy;
use onefuzz::process::Output;
use std::{env, path::PathBuf, process::Stdio};
use tokio::fs;
use tokio::process::Command;
use url::Url;

pub struct Setup {
    pub config: StaticConfig,
}

#[derive(Clone, Debug, Deserialize)]
pub struct DownloadConfig {
    pub tools: Url,
}

impl Setup {
    pub async fn run(&self, onefuzz_path: &Option<PathBuf>) -> Result<()> {
        let download_config = self.get_download_config().await?;

        let onefuzz_path = match onefuzz_path {
            Some(x) => x.to_owned(),
            None => onefuzz::fs::onefuzz_root()?,
        };

        let tools_dir = onefuzz_path.join("tools");
        self.add_tools_path(&tools_dir)?;

        fs::create_dir_all(&tools_dir).await?;
        env::set_current_dir(&onefuzz_path)?;

        az_copy::sync(download_config.tools.to_string(), &tools_dir).await?;
        let output: Output = self
            .setup_command(&onefuzz_path, tools_dir)
            .output()
            .await?
            .into();

        if output.exit_status.success {
            verbose!(
                "setup script succeeded.  stdout:{:?}, stderr:{:?}",
                output.stdout,
                output.stderr
            );
        } else {
            bail!(
                "setup script failed.  stdout:{:?}, stderr:{:?}",
                output.stdout,
                output.stderr
            );
        }

        Ok(())
    }

    fn add_tools_path(&self, path: &PathBuf) -> Result<()> {
        let path_env = env::var("PATH")?;
        let os_path = match env::consts::OS {
            "linux" => format!("{}:{}", path_env, path.join("linux").to_string_lossy()),
            "windows" => format!("{};{}", path_env, path.join("win64").to_string_lossy()),
            _ => unimplemented!("unsupported OS"),
        };
        env::set_var("PATH", os_path);
        Ok(())
    }

    pub async fn launch_supervisor(&self, path: &Option<PathBuf>) -> Result<()> {
        let mut cmd = Command::new("onefuzz-supervisor");
        cmd.arg("run");
        if let Some(path) = path {
            cmd.arg("--config");
            cmd.arg(path);
        }

        let output: Output = cmd.output().await?.into();
        if output.exit_status.success {
            verbose!(
                "supervisor succeeded.  stdout:{:?}, stderr:{:?}",
                output.stdout,
                output.stderr
            );
        } else {
            bail!(
                "supervisor failed.  stdout:{:?}, stderr:{:?}",
                output.stdout,
                output.stderr
            );
        }
        Ok(())
    }

    #[cfg(target_os = "windows")]
    fn setup_command(&self, onefuzz_path: &PathBuf, mut path: PathBuf) -> Command {
        path.push("win64");
        path.push("setup-download.ps1");

        let mut cmd = Command::new("powershell.exe");
        cmd.arg("-ExecutionPolicy");
        cmd.arg("Unrestricted");
        cmd.arg("-File");
        cmd.arg(path);
        cmd.env("ONEFUZZ_ROOT", onefuzz_path);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        cmd
    }

    #[cfg(target_os = "linux")]
    fn setup_command(&self, onefuzz_path: &PathBuf, mut path: PathBuf) -> Command {
        path.push("linux");
        path.push("setup-download.sh");
        let mut cmd = Command::new("bash");
        cmd.arg(path);
        cmd.env("ONEFUZZ_ROOT", onefuzz_path);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        cmd
    }

    async fn get_download_config(&self) -> Result<DownloadConfig> {
        let token = self.config.credentials.access_token().await?;
        let machine_id = onefuzz::machine_id::get_machine_id().await?;
        let mut url = self.config.download_url();
        url.query_pairs_mut()
            .append_pair("machine_id", &machine_id.to_string())
            .append_pair("pool_name", &self.config.pool_name)
            .append_pair("version", env!("ONEFUZZ_VERSION"));

        let response = reqwest::Client::new()
            .get(url)
            .bearer_auth(token.secret().expose_ref())
            .send()
            .await?
            .error_for_status()?;

        let to_download: DownloadConfig = response.json().await?;
        Ok(to_download)
    }
}
