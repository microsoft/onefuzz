// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{config::CommonConfig, heartbeat::*, utils};
use anyhow::Result;
use onefuzz::{
    http::ResponseExt,
    jitter::delay_with_jitter,
    libfuzzer::{LibFuzzer, LibFuzzerMergeOutput},
    syncdir::SyncedDir,
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
    pub input_queue: Option<Url>,
    pub inputs: SyncedDir,
    pub unique_inputs: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub async fn spawn(config: Arc<Config>) -> Result<()> {
    config.unique_inputs.init().await?;
    if let Some(url) = config.input_queue.clone() {
        loop {
            let queue = QueueClient::new(url.clone());
            if let Err(error) = process_message(config.clone(), queue).await {
                error!(
                    "failed to process latest message from notification queue: {}",
                    error
                );
            }
        }
    } else {
        let tmp_dir = "./tmp";
        verbose!("tmp dir reset");
        utils::reset_tmp_dir(tmp_dir).await?;
        config.inputs.init().await?;
        config.inputs.sync_pull().await?;
        sync_and_merge(config.clone(), tmp_dir).await?;
        Ok(())
    }
}

async fn process_message(config: Arc<Config>, mut input_queue: QueueClient) -> Result<()> {
    let hb_client = config.common.init_heartbeat().await?;
    hb_client.alive();
    let tmp_dir = "./tmp";
    verbose!("tmp dir reset");
    utils::reset_tmp_dir(tmp_dir).await?;

    if let Some(msg) = input_queue.pop().await? {
        let input_url = match utils::parse_url_data(msg.data()) {
            Ok(url) => url,
            Err(err) => {
                error!("could not parse input URL from queue message: {}", err);
                return Ok(());
            }
        };

        let input_path = utils::download_input(input_url.clone(), tmp_dir).await?;
        info!("downloaded input to {}", input_path.display());
        sync_and_merge(config.clone(), tmp_dir).await?;

        verbose!("will delete popped message with id = {}", msg.id());

        input_queue.delete(msg).await?;

        verbose!(
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
    input_dir: impl AsRef<Path>,
) -> Result<LibFuzzerMergeOutput> {
    config.unique_inputs.sync_pull().await?;
    match merge_inputs(config.clone(), input_dir).await {
        Ok(result) => {
            if result.added_files_count > 0 {
                info!("Added {} new files to the corpus", result.added_files_count);
                config.unique_inputs.sync_push().await?;
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
    input_dir: impl AsRef<Path>,
) -> Result<LibFuzzerMergeOutput> {
    info!("Merging corpus");
    let merger = LibFuzzer::new(
        &config.target_exe,
        &config.target_options,
        &config.target_env,
    );
    let candidates = vec![&input_dir];
    merger.merge(&config.unique_inputs.path, &candidates).await
}

async fn try_delete_blob(input_url: Url) -> Result<()> {
    let http_client = reqwest::Client::new();
    match http_client
        .delete(input_url)
        .send_retry_default()
        .await?
        .error_for_status_with_body()
        .await
    {
        Ok(_) => Ok(()),
        Err(err) => Err(err.into()),
    }
}
