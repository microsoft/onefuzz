#![allow(clippy::if_same_then_else)]
#![allow(dead_code)]

use anyhow::{anyhow, Result};
use async_trait::async_trait;
use azure_core::{error::HttpError, SeekableStream};
use azure_core::{Body, StatusCode};
use azure_storage::StorageCredentials;
use azure_storage_blobs::prelude::*;
use reqwest::Url;
use std::ops::DerefMut;
use std::pin::Pin;
use std::sync::Arc;
use std::{path::Path, time::Duration};
use tokio::io::AsyncRead;
use tokio::io::AsyncSeekExt;
use tokio::sync::Mutex;

fn create_container_client(log_container: &Url) -> Result<ContainerClient> {
    let account = log_container
        .domain()
        .and_then(|d| d.split('.').next())
        .ok_or(anyhow!("Invalid log container"))?
        .to_owned();
    let container = log_container
        .path_segments()
        .and_then(|mut ps| ps.next())
        .ok_or(anyhow!("Invalid log container"))?
        .to_owned();
    let sas_token = log_container
        .query()
        .ok_or(anyhow!("Invalid log container"))?;

    let sas_credentials = StorageCredentials::sas_token(sas_token)?;
    let client = BlobServiceClient::new(account, sas_credentials);
    Ok(client.container_client(container))
}

async fn get_blob_client(log_container: Url, file_name: &str) -> Result<(BlobClient, u64)> {
    let container_client = create_container_client(&log_container)?;
    let blob_client = container_client.blob_client(file_name);

    match blob_client.get_properties().await {
        Ok(prop) => {
            println!("prop {:?}", prop);
            Ok((blob_client, prop.blob.properties.content_length))
        }
        Err(e) => {
            println!("err {:?}", e);
            match e.downcast_ref::<HttpError>() {
                Some(herr) if herr.status() == StatusCode::NotFound => {
                    blob_client.put_append_blob().await?;
                    Ok((blob_client, 0))
                }
                _ => Err(e.into()),
            }
        }
    }
}

use std::io::Seek;

#[derive(Debug, Clone)]
struct SeekableFile {
    // file: std::pin::Pin<Box<tokio::fs::File>>,
    file: Arc<Mutex<tokio::fs::File>>,
    start_position: u64,
    len: usize,
}

impl SeekableFile {
    fn new(file_path: &Path, start_position: usize) -> Result<Self> {
        let mut file = std::fs::File::open(file_path)?;
        let len = file.metadata().unwrap().len();
        file.seek(std::io::SeekFrom::Start(start_position as u64))
            .unwrap();
        let file = tokio::fs::File::from_std(file);
        Ok(Self {
            //file: Box::pin(file),
            file: Arc::new(Mutex::new(file)),
            start_position: start_position as u64,
            len: len as usize,
        })
    }
}

impl futures::AsyncRead for SeekableFile {
    fn poll_read(
        self: std::pin::Pin<&mut Self>,
        cx: &mut std::task::Context<'_>,
        buf: &mut [u8],
    ) -> std::task::Poll<std::io::Result<usize>> {
        let mut locked = self.file.blocking_lock();
        let file = Pin::new(locked.deref_mut());
        let mut buf = tokio::io::ReadBuf::new(buf);
        match file.poll_read(cx, &mut buf) {
            std::task::Poll::Ready(Ok(())) => {
                std::task::Poll::Ready(Ok(buf.capacity() - buf.remaining()))
            }
            std::task::Poll::Ready(Err(e)) => std::task::Poll::Ready(Err(e)),
            std::task::Poll::Pending => std::task::Poll::Pending,
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

pub async fn continuous_sync_file(log_container: Url, log_path: &Path) -> Result<()> {
    loop {
        let result = sync_file(log_container.clone(), log_path).await;
        if let Err(e) = result {
            warn!("failed to sync log file: {}", e);
        }
        tokio::time::sleep(Duration::from_secs(60)).await;
    }
}

async fn sync_file(log_container: Url, log_path: &Path) -> Result<()> {
    let (blob_client, position) = get_blob_client(log_container, "log.txt").await?;
    let seekable_file = SeekableFile::new(log_path, position as usize)?;
    let f: Box<dyn SeekableStream> = Box::new(seekable_file);
    blob_client.append_block(Body::from(f)).await?;
    Ok(())
}

#[cfg(test)]
mod tests {
    //
    use std::io::Seek;

    use anyhow::Result;
    use tokio::io::{AsyncReadExt, AsyncSeekExt};

    #[allow(clippy::unused_io_amount)]
    #[tokio::test]
    #[ignore]

    async fn test_seek_behavior() -> Result<()> {
        let path = "C:\\temp\\test.ps1";
        let mut std_file = std::fs::File::open(path)?;
        std_file.seek(std::io::SeekFrom::Start(3))?;

        let mut tokio_file = tokio::fs::File::from_std(std_file);

        let buf = &mut [0u8; 5];
        tokio_file.read(buf).await?;
        println!("******** buf {:?}", buf);
        tokio_file.seek(std::io::SeekFrom::Start(0)).await?;
        tokio_file.read(buf).await?;
        println!("******** buf {:?}", buf);

        Ok(())
    }
}
