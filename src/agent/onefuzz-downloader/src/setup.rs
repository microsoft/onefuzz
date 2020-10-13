// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::config::StaticConfig;
use anyhow::Result;
use onefuzz::az_copy;
use onefuzz::process::Output;
use std::path::PathBuf;
use std::process::Stdio;
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
    pub async fn run(&self) -> Result<()> {
        let download_config = self.get_download_config().await?;
        let tools_dir = onefuzz::fs::onefuzz_root()?.join("tools");

        fs::create_dir_all(&tools_dir).await?;
        az_copy::sync(download_config.tools.to_string(), &tools_dir).await?;
        let output: Output = self.setup_command(tools_dir).output().await?.into();

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
    fn setup_command(&self, mut path: PathBuf) -> Command {
        path.push("win64");
        path.push("setup-download.ps1");

        let mut cmd = Command::new("powershell.exe");
        cmd.env(SETUP_PATH_ENV, setup_script);
        cmd.arg("-ExecutionPolicy");
        cmd.arg("Unrestricted");
        cmd.arg("-File");
        cmd.arg(&self.script_path);
        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        cmd
    }

    #[cfg(target_os = "linux")]
    fn setup_command(&self, mut path: PathBuf) -> Command {
        path.push("linux");
        path.push("setup-download.sh");
        let mut cmd = Command::new("bash");
        cmd.arg(path);
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
