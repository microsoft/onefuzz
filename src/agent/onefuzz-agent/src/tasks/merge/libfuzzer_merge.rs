// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::HeartbeatSender,
    utils::{self, default_bool_true},
};
use anyhow::{Context, Result};
use onefuzz::{
    http::ResponseExt,
    jitter::delay_with_jitter,
    libfuzzer::{LibFuzzer, LibFuzzerMergeOutput},
    syncdir::{SyncOperation, SyncedDir},
};
use reqwest::Url;
use reqwest_retry::SendRetry;
use serde::Deserialize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};
use storage_queue::{QueueClient, EMPTY_QUEUE_DELAY};

#[derive(Debug, Deserialize)]
struct QueueMessage {
    content_length: u32,
    url: Url,
}

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub input_queue: Option<QueueClient>,
    pub inputs: Vec<SyncedDir>,
    pub unique_inputs: SyncedDir,
    pub preserve_existing_outputs: bool,

    #[serde(default = "default_bool_true")]
    pub check_fuzzer_help: bool,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub async fn spawn(config: Arc<Config>) -> Result<()> {
    let fuzzer = LibFuzzer::new(
        &config.target_exe,
        &config.target_options,
        &config.target_env,
        &config.common.setup_dir,
    );
    fuzzer.verify(config.check_fuzzer_help, None).await?;

    config.unique_inputs.init().await?;
    if let Some(queue) = config.input_queue.clone() {
        loop {
            if let Err(error) = process_message(config.clone(), queue.clone()).await {
                error!(
                    "failed to process latest message from notification queue: {}",
                    error
                );
            }
        }
    } else {
        for input in config.inputs.iter() {
            input.init().await?;
            input.sync_pull().await?;
        }
        let input_paths = config.inputs.iter().map(|i| &i.local_path).collect();
        sync_and_merge(
            config.clone(),
            input_paths,
            false,
            config.preserve_existing_outputs,
        )
        .await?;
        Ok(())
    }
}

async fn process_message(config: Arc<Config>, input_queue: QueueClient) -> Result<()> {
    let hb_client = config.common.init_heartbeat(None).await?;
    hb_client.alive();
    let tmp_dir = "./tmp";
    debug!("tmp dir reset");
    utils::reset_tmp_dir(tmp_dir).await?;

    if let Some(msg) = input_queue.pop().await? {
        let input_url = msg.parse(|data| {
            let data = std::str::from_utf8(data)?;
            Ok(Url::parse(data)?)
        });
        let input_url: Url = match input_url {
            Ok(url) => url,
            Err(err) => {
                error!("could not parse input URL from queue message: {}", err);
                return Ok(());
            }
        };

        let input_path = utils::download_input(input_url.clone(), tmp_dir).await?;
        info!("downloaded input to {}", input_path.display());
        sync_and_merge(config.clone(), vec![tmp_dir], true, true).await?;

        debug!("will delete popped message with id = {}", msg.id());

        msg.delete().await?;

        debug!(
            "Attempting to delete {} from the candidate container",
            input_url.clone()
        );

        if let Err(e) = try_delete_blob(input_url.clone()).await {
            error!("Failed to delete blob {}", e)
        }
        Ok(())
    } else {
        warn!("no new candidate inputs found, sleeping");
        delay_with_jitter(EMPTY_QUEUE_DELAY).await;
        Ok(())
    }
}

async fn sync_and_merge(
    config: Arc<Config>,
    input_dirs: Vec<impl AsRef<Path>>,
    pull_inputs: bool,
    preserve_existing_outputs: bool,
) -> Result<LibFuzzerMergeOutput> {
    if pull_inputs {
        config.unique_inputs.sync_pull().await?;
    }
    match merge_inputs(config.clone(), input_dirs).await {
        Ok(result) => {
            if result.added_files_count > 0 {
                info!("Added {} new files to the corpus", result.added_files_count);
                config
                    .unique_inputs
                    .sync(SyncOperation::Push, !preserve_existing_outputs)
                    .await?;
            } else {
                info!("No new files added by the merge")
            }
            Ok(result)
        }
        Err(e) => {
            error!("Merge failed : {}", e);
            Err(e)
        }
    }
}

pub async fn merge_inputs(
    config: Arc<Config>,
    candidates: Vec<impl AsRef<Path>>,
) -> Result<LibFuzzerMergeOutput> {
    info!("Merging corpus");
    let merger = LibFuzzer::new(
        &config.target_exe,
        &config.target_options,
        &config.target_env,
        &config.common.setup_dir,
    );
    merger
        .merge(&config.unique_inputs.local_path, &candidates)
        .await
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
