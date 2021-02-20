// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    az_copy, blob::BlobContainerUrl, jitter::delay_with_jitter, monitor::DirectoryMonitor,
    uploader::BlobUploader,
};
use anyhow::{Context, Result};
use futures::stream::StreamExt;
use onefuzz_telemetry::{Event, EventData};
use std::{path::PathBuf, str, time::Duration};
use tokio::fs;

#[derive(Debug, Clone, Copy)]
pub enum SyncOperation {
    Push,
    Pull,
}

const DELAY: Duration = Duration::from_secs(10);
const DEFAULT_CONTINUOUS_SYNC_DELAY_SECONDS: u64 = 60;

#[derive(Debug, Deserialize, Clone, PartialEq, Default)]
pub struct SyncedDir {
    pub path: PathBuf,
    pub url: Option<BlobContainerUrl>,
}

impl SyncedDir {
    pub async fn sync(&self, operation: SyncOperation, delete_dst: bool) -> Result<()> {
        if self.url.is_none() {
            debug!("not syncing as SyncedDir is missing remote URL");
            return Ok(());
        }

        let dir = &self.path;
        let url = self.url.as_ref().unwrap().url();
        let url = url.as_ref();
        debug!("syncing {:?} {}", operation, dir.display());
        match operation {
            SyncOperation::Push => az_copy::sync(dir, url, delete_dst).await,
            SyncOperation::Pull => az_copy::sync(url, dir, delete_dst).await,
        }
    }

    pub fn try_url(&self) -> Result<&BlobContainerUrl> {
        let url = match &self.url {
            Some(x) => x,
            None => bail!("missing URL context"),
        };
        Ok(url)
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
        if self.url.is_none() {
            debug!("not continuously syncing, as SyncDir does not have a remote URL");
            return Ok(());
        }

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

    async fn file_monitor_event(&self, event: Event) -> Result<()> {
        debug!("monitoring {}", self.path.display());
        let mut monitor = DirectoryMonitor::new(self.path.clone());
        monitor.start()?;

        let mut uploader = self.url.as_ref().map(|x| BlobUploader::new(x.url().clone()));

        while let Some(item) = monitor.next().await {
            event!(event.clone(); EventData::Path = item.display().to_string());
            if let Some(uploader) = &mut uploader {
                if let Err(err) = uploader.upload(item.clone()).await {
                    bail!(
                        "Couldn't upload file.  path:{} dir:{} err:{}",
                        item.display(),
                        self.path.display(),
                        err
                    );
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
    pub async fn monitor_results(&self, event: Event) -> Result<()> {
        loop {
            debug!("waiting to monitor {}", self.path.display());

            while fs::metadata(&self.path).await.is_err() {
                debug!("dir {} not ready to monitor, delaying", self.path.display());
                delay_with_jitter(DELAY).await;
            }

            debug!("starting monitor for {}", self.path.display());
            self.file_monitor_event(event.clone()).await?;
        }
    }
}

impl From<PathBuf> for SyncedDir {
    fn from(path: PathBuf) -> Self {
        Self { path, url: None }
    }
}

pub async fn continuous_sync(
    dirs: &[SyncedDir],
    operation: SyncOperation,
    delay_seconds: Option<u64>,
) -> Result<()> {
    let mut should_loop = false;
    for dir in dirs {
        if dir.url.is_some() {
            should_loop = true;
            break;
        }
    }
    if !should_loop {
        debug!("not syncing as SyncDirs do not have remote URLs");
        return Ok(());
    }

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
