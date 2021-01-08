// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    az_copy,
    blob::BlobContainerUrl,
    jitter::delay_with_jitter,
    monitor::DirectoryMonitor,
    telemetry::{Event, EventData},
    uploader::BlobUploader,
};
use anyhow::{Context, Result};
use futures::stream::StreamExt;
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
        let dir = &self.path;
        let url = self.url.url();
        let url = url.as_ref();
        verbose!("syncing {:?} {}", operation, dir.display());
        match operation {
            SyncOperation::Push => az_copy::sync(dir, url, delete_dst).await,
            SyncOperation::Pull => az_copy::sync(url, dir, delete_dst).await,
        }
    }

    pub async fn init_pull(&self) -> Result<()> {
        self.init().await?;
        self.sync(SyncOperation::Pull, false).await
    }

    pub async fn init(&self) -> Result<()> {
        match fs::metadata(&self.path).await {
            Ok(m) => {
                if m.is_dir() {
                    Ok(())
                } else {
                    anyhow::bail!("File with name '{}' already exists", self.path.display());
                }
            }
            Err(_) => fs::create_dir(&self.path).await.with_context(|| {
                format!("unable to create init SyncedDir: {}", self.path.display())
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

    async fn file_uploader_monitor(&self, event: Event) -> Result<()> {
        let url = self.url.url();
        verbose!("monitoring {}", self.path.display());

        let mut monitor = DirectoryMonitor::new(self.path.clone());
        monitor.start()?;
        let mut uploader = BlobUploader::new(url);

        while let Some(item) = monitor.next().await {
            event!(event.clone(); EventData::Path = item.display().to_string());
            if let Err(err) = uploader.upload(item.clone()).await {
                bail!(
                    "Couldn't upload file.  path:{} dir:{} err:{}",
                    item.display(),
                    self.path.display(),
                    err
                );
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
    pub async fn monitor_results(&self, event: Event) -> Result<()> {
        loop {
            verbose!("waiting to monitor {}", self.path.display());

            while fs::metadata(&self.path).await.is_err() {
                verbose!("dir {} not ready to monitor, delaying", self.path.display());
                delay_with_jitter(DELAY).await;
            }

            verbose!("starting monitor for {}", self.path.display());
            self.file_uploader_monitor(event.clone()).await?;
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
