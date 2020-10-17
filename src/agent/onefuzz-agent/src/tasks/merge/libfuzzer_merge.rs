// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::{CommonConfig, SyncedDir},
    heartbeat::*,
    utils,
};
use anyhow::Result;
use onefuzz::{
    http::ResponseExt,
    libfuzzer::{LibFuzzer, LibFuzzerMergeOutput},
};
use reqwest::Url;
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
    pub input_queue: Url,
    pub inputs: SyncedDir,
    pub unique_inputs: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub async fn spawn(config: Arc<Config>) -> Result<()> {
    let hb_client = config.common.init_heartbeat().await?;
    utils::init_dir(&config.unique_inputs.path).await?;
    loop {
        hb_client.alive();
        if let Err(error) = process_message(config.clone()).await {
            error!(
                "failed to process latest message from notification queue: {}",
                error
            );
        }
    }
}

async fn process_message(config: Arc<Config>) -> Result<()> {
    let tmp_dir = "./tmp";

    verbose!("tmp dir reset");

    utils::reset_tmp_dir(tmp_dir).await?;
    utils::sync_remote_dir(&config.unique_inputs, utils::SyncOperation::Pull).await?;

    let mut queue = QueueClient::new(config.input_queue.clone());

    if let Some(msg) = queue.pop().await? {
        let input_url = match utils::parse_url_data(msg.data()) {
            Ok(url) => url,
            Err(err) => {
                error!("could not parse input URL from queue message: {}", err);
                return Ok(());
            }
        };

        let input_path = utils::download_input(input_url.clone(), tmp_dir).await?;
        info!("downloaded input to {}", input_path.display());

        info!("Merging corpus");
        match merge(
            &config.target_exe,
            &config.target_options,
            &config.target_env,
            &config.unique_inputs.path,
            &tmp_dir,
        )
        .await
        {
            Ok(result) if result.added_files_count > 0 => {
                info!("Added {} new files to the corpus", result.added_files_count);
                utils::sync_remote_dir(&config.unique_inputs, utils::SyncOperation::Push).await?;
            }
            Ok(_) => info!("No new files added by the merge"),
            Err(e) => error!("Merge failed : {}", e),
        }

        verbose!("will delete popped message with id = {}", msg.id());

        queue.delete(msg).await?;

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
        tokio::time::delay_for(EMPTY_QUEUE_DELAY).await;
        Ok(())
    }
}

async fn try_delete_blob(input_url: Url) -> Result<()> {
    let http_client = reqwest::Client::new();
    match http_client
        .delete(input_url)
        .send()
        .await?
        .error_for_status_with_body()
        .await
    {
        Ok(_) => Ok(()),
        Err(err) => Err(err.into()),
    }
}

async fn merge(
    target_exe: &Path,
    target_options: &[String],
    target_env: &HashMap<String, String>,
    corpus_dir: &Path,
    candidate_dir: impl AsRef<Path>,
) -> Result<LibFuzzerMergeOutput> {
    let merger = LibFuzzer::new(target_exe, target_options, target_env);
    let candidates = vec![candidate_dir];
    merger.merge(&corpus_dir, &candidates).await
}
