// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{config::CommonConfig, heartbeat::HeartbeatSender, utils};
use anyhow::{Context, Result};
use onefuzz::{
    expand::Expand, fs::set_executable, http::ResponseExt, jitter::delay_with_jitter,
    syncdir::SyncedDir,
};
use reqwest::Url;
use reqwest_retry::SendRetry;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
    sync::Arc,
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

pub async fn spawn(config: Arc<Config>) -> Result<()> {
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

            if let Err(error) = process_message(config.clone(), &input_url, &tmp_dir).await {
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
            warn!("no new candidate inputs found, sleeping");
            delay_with_jitter(EMPTY_QUEUE_DELAY).await;
        };
    }
}

async fn process_message(config: Arc<Config>, input_url: &Url, tmp_dir: &Path) -> Result<()> {
    let input_path =
        utils::download_input(input_url.clone(), &config.unique_inputs.local_path).await?;
    info!("downloaded input to {}", input_path.display());

    info!("Merging corpus");
    match merge(&config, tmp_dir).await {
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
    let expand = Expand::new()
        .machine_id()
        .await?
        .input_marker(&config.supervisor_input_marker)
        .input_corpus(&config.unique_inputs.local_path)
        .target_options(&config.target_options)
        .supervisor_exe(&config.supervisor_exe)
        .supervisor_options(&config.supervisor_options)
        .generated_inputs(output_dir)
        .target_exe(&config.target_exe)
        .setup_dir(&config.common.setup_dir)
        .tools_dir(&config.tools.local_path)
        .job_id(&config.common.job_id)
        .task_id(&config.common.task_id)
        .set_optional_ref(&config.common.microsoft_telemetry_key, |tester, key| {
            tester.microsoft_telemetry_key(key)
        })
        .set_optional_ref(&config.common.instance_telemetry_key, |tester, key| {
            tester.instance_telemetry_key(key)
        });

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
