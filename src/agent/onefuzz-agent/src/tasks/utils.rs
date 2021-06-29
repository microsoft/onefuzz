// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{http::ResponseExt, jitter::delay_with_jitter};
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use std::path::{Path, PathBuf};
use std::time::Duration;
use tokio::{fs, io};

pub async fn download_input(input_url: Url, dst: impl AsRef<Path>) -> Result<PathBuf> {
    let file_name = input_url.path_segments().unwrap().last().unwrap();
    let file_path = dst.as_ref().join(file_name);

    if input_url.scheme().to_lowercase() == "file" {
        let input_file_path = input_url
            .to_file_path()
            .map_err(|_| anyhow!("Invalid file Url"))?;
        fs::copy(&input_file_path, &file_path).await?;
    } else {
        let resp = Client::new()
            .get(input_url)
            .send_retry_default()
            .await
            .context("download_input")?
            .error_for_status_with_body()
            .await
            .context("download_input status body")?;

        let body = resp.bytes().await?;
        let mut body = body.as_ref();

        let file = fs::OpenOptions::new()
            .create(true)
            .write(true)
            .open(&file_path)
            .await?;
        let mut writer = io::BufWriter::new(file);

        io::copy(&mut body, &mut writer).await?;
    }
    Ok(file_path)
}

pub async fn reset_tmp_dir(tmp_dir: impl AsRef<Path>) -> Result<()> {
    let tmp_dir = tmp_dir.as_ref();

    let dir_exists = fs::metadata(tmp_dir).await.is_ok();

    if dir_exists {
        fs::remove_dir_all(tmp_dir).await?;

        debug!("deleted {}", tmp_dir.display());
    }

    fs::create_dir_all(tmp_dir).await?;

    debug!("created {}", tmp_dir.display());

    Ok(())
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
            () = delay_with_jitter(delay) => false,
            () = notify.notified() => true,
        }
    }
}

pub fn parse_key_value(value: String) -> Result<(String, String)> {
    let offset = value
        .find('=')
        .ok_or_else(|| format_err!("invalid key=value, no = found {:?}", value))?;

    Ok((value[..offset].to_string(), value[offset + 1..].to_string()))
}

pub fn default_bool_true() -> bool {
    true
}
