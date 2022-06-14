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
use dunce::canonicalize;
use onefuzz_telemetry::{Event, EventData};
use reqwest::{StatusCode, Url};
use reqwest_retry::{RetryCheck, SendRetry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS};
use serde::{Deserialize, Serialize};
use std::{env::current_dir, path::PathBuf, str, time::Duration};
use tokio::fs;

#[derive(Debug, Clone, Copy)]
pub enum SyncOperation {
    Push,
    Pull,
}

const DELAY: Duration = Duration::from_secs(10);
const DEFAULT_CONTINUOUS_SYNC_DELAY_SECONDS: u64 = 60;

#[derive(Debug, Deserialize, Clone, PartialEq, Eq)]
pub struct SyncedDir {
    #[serde(alias = "local_path", alias = "path")]
    pub local_path: PathBuf,
    #[serde(alias = "remote_path", alias = "url")]
    pub remote_path: Option<BlobContainerUrl>,
}

impl SyncedDir {
    pub fn remote_url(&self) -> Result<BlobContainerUrl> {
        let url = match &self.remote_path {
            Some(url) => url.clone(),
            None => {
                let url = if self.local_path.is_absolute() {
                    Url::from_file_path(self.local_path.clone()).map_err(|err| {
                        anyhow!(
                            "invalid path: {} error: {:?}",
                            self.local_path.display(),
                            err
                        )
                    })?
                } else {
                    let absolute = current_dir()
                        .context("unable to get current directory")?
                        .join(&self.local_path);
                    let canonicalized = canonicalize(&absolute).with_context(|| {
                        format!("unable to canonicalize path: {}", absolute.display())
                    })?;
                    Url::from_file_path(&canonicalized).map_err(|err| {
                        anyhow!("invalid path: {} error: {:?}", canonicalized.display(), err)
                    })?
                };
                BlobContainerUrl::new(url.clone())
                    .with_context(|| format!("unable to create BlobContainerUrl: {}", url))?
            }
        };
        Ok(url)
    }

    pub async fn sync(&self, operation: SyncOperation, delete_dst: bool) -> Result<()> {
        let dir = &self.local_path.join("");

        if let Some(dest) = self.remote_path.clone().and_then(|u| u.as_file_path()) {
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
        } else if let Some(url) = self.remote_path.clone().and_then(|u| u.url().ok()) {
            let url = url.as_ref();
            debug!("syncing {:?} {}", operation, dir.display());
            match operation {
                SyncOperation::Push => az_copy::sync(dir, url, delete_dst).await,
                SyncOperation::Pull => az_copy::sync(url, dir, delete_dst).await,
            }
        } else {
            Ok(())
        }
    }

    pub fn try_url(&self) -> Option<BlobContainerUrl> {
        self.remote_path.clone()
    }

    pub async fn init_pull(&self) -> Result<()> {
        self.init().await.context("init failed")?;
        self.sync(SyncOperation::Pull, false)
            .await
            .context("pull failed")
    }

    pub async fn init(&self) -> Result<()> {
        if let Some(remote_path) = self.remote_path.clone().and_then(|u| u.as_file_path()) {
            fs::create_dir_all(&remote_path).await.with_context(|| {
                format!("unable to create directory: {}", remote_path.display())
            })?;
        }

        match fs::metadata(&self.local_path).await {
            Ok(m) => {
                if m.is_dir() {
                    Ok(())
                } else {
                    anyhow::bail!(
                        "File with name '{}' already exists",
                        self.local_path.display()
                    );
                }
            }
            Err(_) => fs::create_dir_all(&self.local_path).await.with_context(|| {
                format!(
                    "unable to create local SyncedDir: {}",
                    self.local_path.display()
                )
            }),
        }
    }

    pub async fn sync_pull(&self) -> Result<()> {
        self.sync(SyncOperation::Pull, false)
            .await
            .context("sync pull failed")
    }

    pub async fn sync_push(&self) -> Result<()> {
        self.sync(SyncOperation::Push, false)
            .await
            .context("sync push failed")
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
        if let Some(url) = self.remote_path.clone() {
            match url.as_file_path() {
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
                    let url = url.blob(name).url();
                    let blob = BlobClient::new();
                    let result = blob
                        .put(url.clone())
                        .json(data)
                        // Conditional PUT, only if-not-exists.
                        // https://docs.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations
                        .header("If-None-Match", "*")
                        .send_retry(
                            |code| match code {
                                StatusCode::CONFLICT => RetryCheck::Succeed,
                                _ => RetryCheck::Retry,
                            },
                            DEFAULT_RETRY_PERIOD,
                            MAX_RETRY_ATTEMPTS,
                        )
                        .await
                        .context("SyncedDir.upload")?;

                    Ok(result.status() == StatusCode::CREATED)
                }
            }
        } else {
            let path = self.local_path.join(name);
            if !exists(&path).await? {
                let data = serde_json::to_vec(&data)?;
                fs::write(path, data).await?;
                Ok(true)
            } else {
                Ok(false)
            }
        }
    }

    async fn file_monitor_event(
        path: PathBuf,
        url: BlobContainerUrl,
        event: Event,
        ignore_dotfiles: bool,
    ) -> Result<()> {
        debug!("monitoring {}", path.display());

        let mut monitor = DirectoryMonitor::new(path.clone()).await?;

        if let Some(path) = url.as_file_path() {
            fs::create_dir_all(&path).await?;

            while let Some(item) = monitor.next_file().await? {
                let file_name = item
                    .file_name()
                    .ok_or_else(|| anyhow!("invalid file path"))?;
                let file_name_str = file_name.to_string_lossy();

                // explicitly ignore azcopy temporary files
                // https://github.com/Azure/azure-storage-azcopy/blob/main/ste/xfer-remoteToLocal-file.go#L35
                if file_name_str.starts_with(".azDownload-") {
                    continue;
                }

                if ignore_dotfiles && file_name_str.starts_with('.') {
                    continue;
                }

                event!(event.clone(); EventData::Path = file_name_str);
                let destination = path.join(file_name);
                if let Err(err) = fs::copy(&item, &destination).await {
                    let error_message = format!(
                        "Couldn't upload file.  path:{:?} dir:{:?} err:{:?}",
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
            let mut uploader = BlobUploader::new(url.url()?);

            while let Some(item) = monitor.next_file().await? {
                let file_name = item
                    .file_name()
                    .ok_or_else(|| anyhow!("invalid file path"))?;
                let file_name_str = file_name.to_string_lossy();

                // explicitly ignore azcopy temporary files
                // https://github.com/Azure/azure-storage-azcopy/blob/main/ste/xfer-remoteToLocal-file.go#L35
                if file_name_str.starts_with(".azDownload-") {
                    continue;
                }

                if ignore_dotfiles && file_name_str.starts_with('.') {
                    continue;
                }

                event!(event.clone(); EventData::Path = file_name_str);
                if let Err(err) = uploader.upload(item.clone()).await {
                    let error_message = format!(
                        "Couldn't upload file.  path:{} dir:{} err:{:?}",
                        item.display(),
                        path.display(),
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
        if let Some(url) = self.remote_path.clone() {
            loop {
                debug!("waiting to monitor {}", self.local_path.display());

                while fs::metadata(&self.local_path).await.is_err() {
                    debug!(
                        "dir {} not ready to monitor, delaying",
                        self.local_path.display()
                    );
                    delay_with_jitter(DELAY).await;
                }

                debug!("starting monitor for {}", self.local_path.display());
                Self::file_monitor_event(
                    self.local_path.clone(),
                    url.clone(),
                    event.clone(),
                    ignore_dotfiles,
                )
                .await?;
            }
        } else {
            Ok(())
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

#[cfg(test)]
mod tests {
    use super::SyncedDir;
    use anyhow::{anyhow, Result};
    use dunce::canonicalize;
    use std::env::current_dir;
    use std::path::PathBuf;

    #[test]
    fn test_synceddir_relative_remote_url() -> Result<()> {
        let path = PathBuf::from("Cargo.toml");
        let expected = canonicalize(current_dir()?.join(&path))?;
        let dir = SyncedDir {
            local_path: path,
            remote_path: None,
        };
        let blob_path = dir
            .remote_url()?
            .as_file_path()
            .ok_or(anyhow!("as_file_path failed"))?;
        assert_eq!(expected, blob_path);
        Ok(())
    }
}
