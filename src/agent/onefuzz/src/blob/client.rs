// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};

use anyhow::{Context, Result};
use futures::stream::TryStreamExt;
use reqwest::{Body, RequestBuilder, Response, Url};
use reqwest_retry::SendRetry;
use serde::Serialize;
use tokio::{fs, io};
use tokio_util::codec;

#[derive(Clone)]
pub struct BlobClient {
    client: reqwest::Client,
}

impl Default for BlobClient {
    fn default() -> Self {
        Self::new()
    }
}

impl BlobClient {
    pub fn new() -> Self {
        let client = reqwest::Client::new();

        Self { client }
    }

    pub async fn get(&self, url: &Url) -> Result<Response> {
        let url = url.clone();

        let r = self
            .client
            .get(url)
            .send_retry_default()
            .await
            .context("BlobClient.get")?
            .error_for_status()
            .context("BlobClient.get status")?;

        Ok(r)
    }

    pub async fn get_data(&self, url: &Url) -> Result<Vec<u8>> {
        let r = self.get(url).await?;
        let b = r.bytes().await?;

        Ok(b.to_vec())
    }

    pub async fn get_file(&self, url: &Url, dst: impl AsRef<Path>) -> Result<PathBuf> {
        let dst = dst.as_ref();

        let data = self.get_data(url).await?;
        fs::write(dst, &data).await?;

        Ok(dst.to_owned())
    }

    pub fn put(&self, url: Url) -> RequestBuilder {
        self.client.put(url).header("x-ms-blob-type", "BlockBlob")
    }

    pub async fn put_data(&self, url: Url, data: impl Into<Body>) -> Result<Response> {
        self.put(url)
            .body(data)
            .send_retry_default()
            .await
            .context("BlobClient.put_data")
    }

    pub async fn put_json<I>(&self, url: Url, item: I) -> Result<Response>
    where
        I: Serialize,
    {
        self.put(url)
            .json(&item)
            .send_retry_default()
            .await
            .context("BlobClient.put_json")
    }

    pub async fn put_file(&self, file_url: Url, file_path: impl AsRef<Path>) -> Result<Response> {
        let file_path = file_path.as_ref();

        let metadata = fs::metadata(file_path).await?;
        let file_len = metadata.len();

        let file = fs::File::open(file_path).await?;
        let reader = io::BufReader::new(file);
        let codec = codec::BytesCodec::new();
        let file_stream = codec::FramedRead::new(reader, codec).map_ok(bytes::BytesMut::freeze);

        let body = reqwest::Body::wrap_stream(file_stream);
        let content_length = format!("{}", file_len);

        self.put(file_url)
            .header("Content-Length", &content_length)
            .body(body)
            .send_retry_default()
            .await
            .context("BlobClient.put_file")
    }
}
