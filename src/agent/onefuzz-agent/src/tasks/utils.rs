// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::Result;
use async_trait::async_trait;
use futures::{future::Future, stream::StreamExt};
use onefuzz::{
    az_copy,
    monitor::DirectoryMonitor,
    telemetry::{Event::new_result, EventData},
};
use reqwest::Url;
use tokio::{fs, io};

use crate::tasks::config::SyncedDir;

#[derive(Debug)]
pub enum SyncOperation {
    Push,
    Pull,
}

pub async fn download_input(input_url: Url, dst: impl AsRef<Path>) -> Result<PathBuf> {
    let file_name = input_url.path_segments().unwrap().last().unwrap();
    let file_path = dst.as_ref().join(file_name);

    let resp = reqwest::get(input_url).await?;

    let body = resp.bytes().await?;
    let mut body = body.as_ref();

    let file = fs::OpenOptions::new()
        .create(true)
        .write(true)
        .open(&file_path)
        .await?;
    let mut writer = io::BufWriter::new(file);

    io::copy(&mut body, &mut writer).await?;

    Ok(file_path)
}

pub async fn reset_tmp_dir(tmp_dir: impl AsRef<Path>) -> Result<()> {
    let tmp_dir = tmp_dir.as_ref();

    let dir_exists = fs::metadata(tmp_dir).await.is_ok();

    if dir_exists {
        fs::remove_dir_all(tmp_dir).await?;

        verbose!("deleted {}", tmp_dir.display());
    }

    fs::create_dir_all(tmp_dir).await?;

    verbose!("created {}", tmp_dir.display());

    Ok(())
}

pub async fn sync_remote_dir(sync_dir: &SyncedDir, sync_operation: SyncOperation) -> Result<()> {
    let dir = &sync_dir.path;
    let url = sync_dir.url.url();
    let url = url.as_ref();
    info!("syncing {:?} {:?}", sync_operation, sync_dir.path);
    match sync_operation {
        SyncOperation::Push => az_copy::sync(dir, url).await,
        SyncOperation::Pull => az_copy::sync(url, dir).await,
    }
}

pub async fn init_dir(path: impl AsRef<Path>) -> Result<()> {
    let path = path.as_ref();

    match fs::metadata(path).await {
        Ok(m) => {
            if m.is_dir() {
                Ok(())
            } else {
                anyhow::bail!("File with name '{}' already exists", path.display());
            }
        }
        Err(_) => fs::create_dir(path).await.map_err(|e| e.into()),
    }
}

pub fn parse_url_data(data: &[u8]) -> Result<Url> {
    let text = std::str::from_utf8(data)?;
    let url = Url::parse(text)?;

    Ok(url)
}

#[async_trait]
pub trait CheckNotify {
    async fn is_notified(&self, delay: Duration) -> bool;
}

#[async_trait]
impl CheckNotify for tokio::sync::Notify {
    async fn is_notified(&self, delay: Duration) -> bool {
        let notify = self;
        tokio::select! {
            () = tokio::time::delay_for(delay) => false,
            () = notify.notified() => true,
        }
    }
}

const DELAY: Duration = Duration::from_secs(10);

pub fn file_uploader_monitor(synced_dir: SyncedDir) -> Result<impl Future> {
    verbose!("monitoring {}", synced_dir.path.display());

    let dir = synced_dir.path;
    let url = synced_dir.url;

    let mut monitor = DirectoryMonitor::new(&dir);
    monitor.start()?;

    let monitor = monitor.for_each(move |item| {
        verbose!("saw item = {}", item.display());

        let url = url.clone();

        async move {
            event!(new_result; EventData::Path = item.display().to_string());

            let mut uploader = onefuzz::uploader::BlobUploader::new(url.url());
            let result = uploader.upload(item.clone()).await;

            if let Err(err) = result {
                error!("couldn't upload item = {}, error = {}", item.display(), err);
            } else {
                verbose!("uploaded item = {}", item.display());
            }
        }
    });

    verbose!("done monitoring {}", dir.display());

    Ok(monitor)
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
pub async fn monitor_result_dir(synced_dir: SyncedDir) -> Result<()> {
    loop {
        verbose!("waiting to monitor {}", synced_dir.path.display());

        while fs::metadata(&synced_dir.path).await.is_err() {
            verbose!(
                "dir {} not ready to monitor, delaying",
                synced_dir.path.display()
            );
            tokio::time::delay_for(DELAY).await;
        }

        verbose!("starting monitor for {}", synced_dir.path.display());
        file_uploader_monitor(synced_dir.clone())?.await;
    }
}

pub fn parse_key_value(value: String) -> Result<(String, String)> {
    let offset = value
        .find('=')
        .ok_or_else(|| format_err!("invalid key=value, no = found {:?}", value))?;

    Ok((value[..offset].to_string(), value[offset + 1..].to_string()))
}
