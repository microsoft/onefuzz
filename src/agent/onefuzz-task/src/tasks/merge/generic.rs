// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::HeartbeatSender,
    utils::{self, try_resolve_setup_relative_path},
};
use anyhow::{Context, Result};
use onefuzz::{
    expand::{Expand, GetExpand}, fs::set_executable, http::ResponseExt, jitter::delay_with_jitter,
    syncdir::SyncedDir,
};
use reqwest::Url;
use reqwest_retry::SendRetry;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
};
use storage_queue::{QueueClient, EMPTY_QUEUE_DELAY};
use tokio::process::Command;

#[derive(Debug, Deserialize)]
pub struct Config {
    pub supervisor_exe: String,
    pub supervisor_options: Vec<String>,
    pub supervisor_env: HashMap<String, String>,
    pub supervisor_input_marker: String,
    pub target_exe: PathBuf,
    pub target_options: Vec<String>,
    pub target_options_merge: bool,
    pub tools: SyncedDir,
    pub input_queue: Url,
    pub inputs: SyncedDir,
    pub unique_inputs: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

impl GetExpand for Config {
    fn get_expand<'a>(&'a self) -> Result<Expand<'a>> {
        Ok(
            self.common.get_expand()?
            .input_marker(&self.supervisor_input_marker)
            .input_corpus(&self.unique_inputs.local_path) // TODO: verify that this is correct (should it be self.inputs.local_path?)
            .target_exe(&self.target_exe)
            .target_options(&self.target_options)
            .supervisor_exe(&self.supervisor_exe)
            .supervisor_options(&self.supervisor_options)
            .tools_dir(self.tools.local_path.to_string_lossy().into_owned())
            .generated_inputs(&self.inputs.local_path)
        )
    }
}

pub async fn spawn(config: &Config) -> Result<()> {
    config.tools.init_pull().await?;
    set_executable(&config.tools.local_path).await?;

    config.unique_inputs.init().await?;
    let hb_client = config.common.init_heartbeat(None).await?;
    loop {
        hb_client.alive();
        let tmp_dir = PathBuf::from("./tmp");
        debug!("tmp dir reset");
        utils::reset_tmp_dir(&tmp_dir).await?;
        config.unique_inputs.sync_pull().await?;
        let queue = QueueClient::new(config.input_queue.clone())?;
        if let Some(msg) = queue.pop().await? {
            let input_url = msg.parse(utils::parse_url_data);
            let input_url = match input_url {
                Ok(url) => url,
                Err(err) => {
                    error!("could not parse input URL from queue message: {}", err);
                    return Ok(());
                }
            };

            if let Err(error) = process_message(config, &input_url, &tmp_dir).await {
                error!(
                    "failed to process latest message from notification queue: {}",
                    error
                );
            } else {
                debug!("will delete popped message with id = {}", msg.id());

                msg.delete().await?;

                debug!(
                    "Attempting to delete {} from the candidate container",
                    input_url.clone()
                );

                if let Err(e) = try_delete_blob(input_url.clone()).await {
                    error!("Failed to delete blob {}", e)
                }
            }
        } else {
            debug!("no new candidate inputs found, sleeping");
            delay_with_jitter(EMPTY_QUEUE_DELAY).await;
        };
    }
}

async fn process_message(config: &Config, input_url: &Url, tmp_dir: &Path) -> Result<()> {
    let input_path =
        utils::download_input(input_url.clone(), &config.unique_inputs.local_path).await?;
    info!("downloaded input to {}", input_path.display());

    info!("Merging corpus");
    match merge(config, tmp_dir).await {
        Ok(_) => {
            // remove the 'queue' folder
            let mut queue_dir = tmp_dir.to_path_buf();
            queue_dir.push("queue");
            let _delete_output = tokio::fs::remove_dir_all(queue_dir).await;
            let synced_dir = SyncedDir {
                local_path: tmp_dir.to_path_buf(),
                remote_path: config.unique_inputs.remote_path.clone(),
            };
            synced_dir.sync_push().await?
        }
        Err(e) => error!("Merge failed : {}", e),
    }
    Ok(())
}

async fn try_delete_blob(input_url: Url) -> Result<()> {
    let http_client = reqwest::Client::new();
    http_client
        .delete(input_url)
        .send_retry_default()
        .await
        .context("try_delete_blob")?
        .error_for_status_with_body()
        .await
        .context("try_delete_blob status body")?;
    Ok(())
}

async fn merge(config: &Config, output_dir: impl AsRef<Path>) -> Result<()> {
    let target_exe =
        try_resolve_setup_relative_path(&config.common.setup_dir, &config.target_exe).await?;

    let expand = config.get_expand()?
        .generated_inputs(output_dir)
        .target_exe(&target_exe);

    let supervisor_path = expand.evaluate_value(&config.supervisor_exe)?;

    let mut cmd = Command::new(supervisor_path);

    cmd.kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    for (k, v) in &config.supervisor_env {
        cmd.env(k, expand.evaluate_value(v)?);
    }

    for arg in expand.evaluate(&config.supervisor_options)? {
        cmd.arg(arg);
    }

    if !config.target_options_merge {
        for arg in expand.evaluate(&config.target_options)? {
            cmd.arg(arg);
        }
    }

    info!("Starting merge '{:?}'", cmd);
    cmd.spawn()?.wait_with_output().await?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use proptest::prelude::*;
    use onefuzz::expand::{GetExpand, PlaceHolder};

    use crate::config_test_utils::GetExpandFields;

    use super::Config;

    impl GetExpandFields for Config {
        fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)> {
            let mut params = self.common.get_expand_fields();
            params.push((PlaceHolder::Input, self.supervisor_input_marker.clone()));
            params.push((PlaceHolder::InputCorpus, dunce::canonicalize(&self.unique_inputs.local_path).unwrap().to_string_lossy().to_string()));
            params.push((PlaceHolder::TargetExe, dunce::canonicalize(&self.target_exe).unwrap().to_string_lossy().to_string()));
            params.push((PlaceHolder::TargetOptions, self.target_options.join(" ")));
            params.push((PlaceHolder::SupervisorExe, dunce::canonicalize(&self.supervisor_exe).unwrap().to_string_lossy().to_string()));
            params.push((PlaceHolder::SupervisorOptions, self.supervisor_options.join(" ")));
            params.push((PlaceHolder::ToolsDir, dunce::canonicalize(&self.tools.local_path).unwrap().to_string_lossy().to_string()));
            params.push((PlaceHolder::GeneratedInputs, dunce::canonicalize(&self.inputs.local_path).unwrap().to_string_lossy().to_string()));

            params
        }
    }

    proptest! {
        #[test]
        fn test_get_expand_values_match_config(
            config in any::<Config>(),
        ) {
            let expand = match config.get_expand() {
                Ok(expand) => expand,
                Err(err) => panic!("error getting expand: {}", err),
            };
            let params = config.get_expand_fields();

            for (param, expected) in params.iter() {
                let evaluated = expand.evaluate_value(format!("{}", param.get_string())).unwrap();
                assert_eq!(evaluated, *expected, "placeholder {} did not match expected value", param.get_string());
            }
        }
    }
}
