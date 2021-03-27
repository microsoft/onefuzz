// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    az_copy,
    blob::{BlobClient, BlobContainerUrl},
    fs::{exists, sync, SyncPath},
    jitter::delay_with_jitter,
    monitor::DirectoryMonitor,
    uploader::BlobUploader,
};
use anyhow::{Context, Result};
use futures::stream::StreamExt;
use onefuzz_telemetry::{Event, EventData};
use reqwest::StatusCode;
use reqwest_retry::{SendRetry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS};
use serde::{Deserialize, Serialize};
use std::{path::PathBuf, str, time::Duration};
use tokio::fs;

#[derive(Debug, Clone, Copy)]
pub enum SyncOperation {
    Push,
    Pull,
}

const DELAY: Duration = Duration::from_secs(10);
const DEFAULT_CONTINUOUS_SYNC_DELAY_SECONDS: u64 = 60;

#[derive(Debug, Deserialize, Clone, PartialEq)]
pub struct SyncedDir {
    pub path: PathBuf,
    pub url: BlobContainerUrl,
}

impl SyncedDir {
    pub async fn sync(&self, operation: SyncOperation, delete_dst: bool) -> Result<()> {
        let dir = &self.path.join("");
        if let Some(dest) = self.url.as_file_path() {
            debug!("syncing {:?} {}", operation, dest.display());
            match operation {
                SyncOperation::Push => {
                    sync(
                        SyncPath::dir(dir),
                        SyncPath::dir(dest.as_path()),
                        delete_dst,
                    )
                    .await
                }
                SyncOperation::Pull => {
                    sync(
                        SyncPath::dir(dest.as_path()),
                        SyncPath::dir(dir),
                        delete_dst,
                    )
                    .await
                }
            }
        } else {
            let url = self.url.url();
            let url = url.as_ref();
            debug!("syncing {:?} {}", operation, dir.display());
            match operation {
                SyncOperation::Push => az_copy::sync(dir, url, delete_dst).await,
                SyncOperation::Pull => az_copy::sync(url, dir, delete_dst).await,
            }
        }
    }

    pub fn try_url(&self) -> Result<&BlobContainerUrl> {
        Ok(&self.url)
    }

    pub async fn init_pull(&self) -> Result<()> {
        self.init().await?;
        self.sync(SyncOperation::Pull, false).await
    }

    pub async fn init(&self) -> Result<()> {
        if let Some(remote_path) = self.url.as_file_path() {
            fs::create_dir_all(remote_path).await?;
        }

        match fs::metadata(&self.path).await {
            Ok(m) => {
                if m.is_dir() {
                    Ok(())
                } else {
                    anyhow::bail!("File with name '{}' already exists", self.path.display());
                }
            }
            Err(_) => fs::create_dir_all(&self.path).await.with_context(|| {
                format!("unable to create local SyncedDir: {}", self.path.display())
            }),
        }
    }

    pub async fn sync_pull(&self) -> Result<()> {
        self.sync(SyncOperation::Pull, false).await
    }

    pub async fn sync_push(&self) -> Result<()> {
        self.sync(SyncOperation::Push, false).await
    }

    pub async fn continuous_sync(
        &self,
        operation: SyncOperation,
        delay_seconds: Option<u64>,
    ) -> Result<()> {
        let delay_seconds = delay_seconds.unwrap_or(DEFAULT_CONTINUOUS_SYNC_DELAY_SECONDS);
        if delay_seconds == 0 {
            return Ok(());
        }
        let delay = Duration::from_secs(delay_seconds);

        loop {
            self.sync(operation, false).await?;
            delay_with_jitter(delay).await;
        }
    }

    // Conditionally upload a report, if it would not be a duplicate.
    pub async fn upload<T: Serialize>(&self, name: &str, data: &T) -> Result<bool> {
        match self.url.as_file_path() {
            Some(path) => {
                let path = path.join(name);
                if !exists(&path).await? {
                    let data = serde_json::to_vec(&data)?;
                    fs::write(path, data).await?;
                    Ok(true)
                } else {
                    Ok(false)
                }
            }
            None => {
                let url = self.url.blob(name).url();
                let blob = BlobClient::new();
                let result = blob
                    .put(url.clone())
                    .json(data)
                    // Conditional PUT, only if-not-exists.
                    // https://docs.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations
                    .header("If-None-Match", "*")
                    .send_retry(
                        vec![StatusCode::CONFLICT],
                        DEFAULT_RETRY_PERIOD,
                        MAX_RETRY_ATTEMPTS,
                    )
                    .await
                    .context("Uploading blob")?;

                Ok(result.status() == StatusCode::CREATED)
            }
        }
    }

    async fn file_monitor_event(&self, event: Event, ignore_dotfiles: bool) -> Result<()> {
        debug!("monitoring {}", self.path.display());
        let mut monitor = DirectoryMonitor::new(self.path.clone());
        monitor.start()?;

        if let Some(path) = self.url.as_file_path() {
            fs::create_dir_all(&path).await?;

            while let Some(item) = monitor.next().await {
                let file_name = item
                    .file_name()
                    .ok_or_else(|| anyhow!("invalid file path"))?;
                if ignore_dotfiles && file_name.to_string_lossy().starts_with(".") {
                    continue;
                }

                event!(event.clone(); EventData::Path = file_name.to_string_lossy());
                let destination = path.join(file_name);
                if let Err(err) = fs::copy(&item, &destination).await {
                    let error_message = format!(
                        "Couldn't upload file.  path:{:?} dir:{:?} err:{}",
                        item, destination, err
                    );

                    if !item.exists() {
                        // guarding against cases where a temporary file was detected
                        // but was deleted before the copy
                        debug!("{}", error_message);
                        continue;
                    }
                    bail!("{}", error_message);
                }
            }
        } else {
            let mut uploader = BlobUploader::new(self.url.url().clone());

            while let Some(item) = monitor.next().await {
                let file_name = item
                    .file_name()
                    .ok_or_else(|| anyhow!("invalid file path"))?;
                if ignore_dotfiles && file_name.to_string_lossy().starts_with(".") {
                    continue;
                }
                event!(event.clone(); EventData::Path = item.display().to_string());

                if let Err(err) = uploader.upload(item.clone()).await {
                    let error_message = format!(
                        "Couldn't upload file.  path:{} dir:{} err:{}",
                        item.display(),
                        self.path.display(),
                        err
                    );

                    if !item.exists() {
                        // guarding against cases where a temporary file was detected
                        // but was deleted before the upload
                        debug!("{}", error_message);
                        continue;
                    }
                    bail!("{}", error_message);
                }
            }
        }
        Ok(())
    }

    /// Monitor a directory for results.
    ///
    /// This function does not require the directory to exist before it is called.
    /// If the directory is reset (unlinked and recreated), this function will stop
    /// listening to the original filesystem node, and begin watching the new one
    /// once it has been created.
    ///
    /// The intent of this is to support use cases where we usually want a directory
    /// to be initialized, but a user-supplied binary, (such as AFL) logically owns
    /// a directory, and may reset it.
    pub async fn monitor_results(&self, event: Event, ignore_dotfiles: bool) -> Result<()> {
        loop {
            debug!("waiting to monitor {}", self.path.display());

            while fs::metadata(&self.path).await.is_err() {
                debug!("dir {} not ready to monitor, delaying", self.path.display());
                delay_with_jitter(DELAY).await;
            }

            debug!("starting monitor for {}", self.path.display());
            self.file_monitor_event(event.clone(), ignore_dotfiles)
                .await?;
        }
    }
}

pub async fn continuous_sync(
    dirs: &[SyncedDir],
    operation: SyncOperation,
    delay_seconds: Option<u64>,
) -> Result<()> {
    let delay_seconds = delay_seconds.unwrap_or(DEFAULT_CONTINUOUS_SYNC_DELAY_SECONDS);
    if delay_seconds == 0 {
        return Ok(());
    }

    let delay = Duration::from_secs(delay_seconds);

    loop {
        for dir in dirs {
            dir.sync(operation, false).await?;
        }
        delay_with_jitter(delay).await;
    }
}
