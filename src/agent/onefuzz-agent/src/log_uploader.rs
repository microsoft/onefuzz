#![allow(clippy::if_same_then_else)]
#![allow(dead_code)]

use anyhow::{anyhow, Result};
use async_trait::async_trait;
use azure_core::{error::HttpError, Body, SeekableStream, StatusCode};
use azure_storage::StorageCredentials;
use azure_storage_blobs::prelude::{BlobClient, BlobServiceClient, ContainerClient};
use onefuzz::utils::CheckNotify;
use reqwest::Url;
use std::{ops::DerefMut, path::Path, pin::Pin, sync::Arc, time::Duration};

use tokio::{
    io::{AsyncRead, AsyncSeekExt},
    sync::Mutex,
};

// append blob has a limit of 50,000 blocks, each block can be up to 4MiB
// https://learn.microsoft.com/en-us/rest/api/storageservices/understanding-block-blobs--append-blobs--and-page-blobs#about-append-blobs
const MAX_BLOB_BLOCK_SIZE: u64 = 4 * 1024 * 1024;

const UPLOAD_INTERVAL: Duration = Duration::from_secs(60);
const LOG_FLUSH_TIMEOUT: Duration = Duration::from_secs(60);

fn create_container_client(log_container: &Url) -> Result<ContainerClient> {
    let account = log_container
        .domain()
        .and_then(|d| d.split('.').next())
        .ok_or(anyhow!("unable to retrieve the account"))?
        .to_owned();
    let container = log_container
        .path_segments()
        .and_then(|mut ps| ps.next())
        .ok_or(anyhow!("Unable the container from the url"))?
        .to_owned();
    let sas_token = log_container
        .query()
        .ok_or(anyhow!("Unable to retrieve the sas from the url"))?;

    let sas_credentials = StorageCredentials::sas_token(sas_token)?;
    let client = BlobServiceClient::new(account, sas_credentials);
    Ok(client.container_client(container))
}

async fn get_blob_client(log_container: Url, file_name: &str) -> Result<(BlobClient, u64)> {
    let container_client = create_container_client(&log_container)?;
    let blob_client = container_client.blob_client(file_name);

    match blob_client.get_properties().await {
        Ok(prop) => {
            debug!("prop {:?}", prop);
            Ok((blob_client, prop.blob.properties.content_length))
        }
        Err(e) => match e.downcast_ref::<HttpError>() {
            Some(herr) if herr.status() == StatusCode::NotFound => {
                blob_client.put_append_blob().await?;
                Ok((blob_client, 0))
            }
            _ => Err(e.into()),
        },
    }
}

use std::io::Seek;

#[derive(Debug, Clone)]
struct SeekableFile {
    file: Arc<Mutex<tokio::fs::File>>,
    start_position: u64,
    pub len: usize,
}

/// Implement a seekable stream over a file to avoid reading the whole file in memory
impl SeekableFile {
    fn new(file_path: &Path, start_position: usize) -> Result<Self> {
        let mut file = std::fs::File::open(file_path)?;
        let remaining_size = file.metadata().unwrap().len() - (start_position as u64);
        let len = std::cmp::min(remaining_size, MAX_BLOB_BLOCK_SIZE);
        file.seek(std::io::SeekFrom::Start(start_position as u64))
            .unwrap();
        let file = tokio::fs::File::from_std(file);
        Ok(Self {
            file: Arc::new(Mutex::new(file)),
            start_position: start_position as u64,
            len: len as usize,
        })
    }

    fn is_empty(&self) -> bool {
        self.len == 0
    }
}

impl futures::AsyncRead for SeekableFile {
    fn poll_read(
        self: std::pin::Pin<&mut Self>,
        cx: &mut std::task::Context<'_>,
        buf: &mut [u8],
    ) -> std::task::Poll<std::io::Result<usize>> {
        let mut locked = self.file.try_lock();
        loop {
            if let Ok(mut locked) = locked {
                let file = Pin::new(locked.deref_mut());
                let mut buf = tokio::io::ReadBuf::new(buf);
                match file.poll_read(cx, &mut buf) {
                    std::task::Poll::Ready(Ok(())) => {
                        return std::task::Poll::Ready(Ok(buf.capacity() - buf.remaining()));
                    }
                    std::task::Poll::Ready(Err(e)) => {
                        return std::task::Poll::Ready(Err(e));
                    }
                    std::task::Poll::Pending => {
                        return std::task::Poll::Pending;
                    }
                }
            }
            locked = self.file.try_lock();
        }
    }
}

#[async_trait]
impl SeekableStream for SeekableFile {
    async fn reset(&mut self) -> azure_core::error::Result<()> {
        let mut file = self.file.lock().await;
        file.seek(std::io::SeekFrom::Start(self.start_position))
            .await?;
        Ok(())
    }

    fn len(&self) -> usize {
        self.len
    }
}

#[derive(Debug)]
pub struct Uploader {
    notify: Arc<tokio::sync::Notify>,
    uploader: Mutex<Option<tokio::task::JoinHandle<anyhow::Result<()>>>>,
}

impl Uploader {
    pub fn start_sync(log_container: Url, log_path: impl AsRef<Path>, log_blob_name: &str) -> Self {
        let notify = Arc::new(tokio::sync::Notify::new());
        let log_path = log_path.as_ref().to_path_buf();
        let log_blob_name = log_blob_name.to_string();
        let cloned_notify = notify.clone();
        let mut stopped = false;
        let uploader = tokio::spawn(async move {
            loop {
                let result =
                    sync_file(log_container.clone(), &log_path, &log_blob_name.to_string()).await;
                let count = match result {
                    Err(e) => {
                        warn!(
                            "failed to sync log file log_path: '{}' log_container '{}': {}",
                            log_container,
                            log_path.display(),
                            e
                        );
                        0
                    }
                    Ok(count) => count,
                };

                if stopped {
                    if count == 0 {
                        break;
                    } else {
                        continue;
                    }
                }
                stopped = cloned_notify.is_notified(UPLOAD_INTERVAL).await;
            }
            Ok(())
        });
        Self {
            notify,
            uploader: Mutex::new(Some(uploader)),
        }
    }

    pub async fn stop_sync(&self) -> Result<()> {
        let mut uploader_lock = self.uploader.lock().await;
        if let Some(uploader) = uploader_lock.take() {
            self.notify.notify_one();
            let _ = tokio::time::timeout(LOG_FLUSH_TIMEOUT, uploader).await?;
        }
        Ok(())
    }
}

async fn sync_file(
    log_container: Url,
    log_path: impl AsRef<Path>,
    log_blob_name: &str,
) -> Result<usize> {
    if !log_path.as_ref().exists() {
        return Ok(0);
    }

    let (blob_client, position) = get_blob_client(log_container, log_blob_name).await?;
    let seekable_file = SeekableFile::new(log_path.as_ref(), position as usize)?;

    if seekable_file.is_empty() {
        return Ok(0);
    }
    let len = seekable_file.len();
    let f: Box<dyn SeekableStream> = Box::new(seekable_file);
    blob_client.append_block(Body::from(f)).await?;
    Ok(len)
}
