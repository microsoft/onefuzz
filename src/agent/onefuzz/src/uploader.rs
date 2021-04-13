// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::Path;

use anyhow::{Context, Result};
use futures::stream::TryStreamExt;
use reqwest::{Body, Client, Response, StatusCode, Url};
use reqwest_retry::{
    send_retry_reqwest_default, RetryCheck, SendRetry, DEFAULT_RETRY_PERIOD, MAX_RETRY_ATTEMPTS,
};
use serde::Serialize;
use tokio::{fs, io};
use tokio_util::codec;

#[derive(Clone)]
pub struct BlobUploader {
    client: Client,
    url: Url,
}

impl BlobUploader {
    pub fn new(url: Url) -> Self {
        let client = Client::new();

        Self { client, url }
    }

    pub async fn upload(&mut self, file_path: impl AsRef<Path>) -> Result<Response> {
        let file_path = file_path.as_ref();

        let file_name = file_path.file_name().unwrap().to_str().unwrap();

        let metadata = fs::metadata(file_path).await?;
        let file_len = metadata.len();

        let url = {
            let url_path = self.url.path();
            let blob_path = format!("{}/{}", url_path, file_name);
            let mut url = self.url.clone();
            url.set_path(&blob_path);
            url
        };

        // Check if the file already exists before uploading
        if let Ok(head) = self
            .client
            .head(url.clone())
            .send_retry(
                |code| match code {
                    StatusCode::NOT_FOUND => RetryCheck::Fail,
                    _ => RetryCheck::Retry,
                },
                DEFAULT_RETRY_PERIOD,
                MAX_RETRY_ATTEMPTS,
            )
            .await
        {
            if head.status() == StatusCode::OK {
                return Ok(head);
            }
        }

        let content_length = format!("{}", file_len);

        let resp = send_retry_reqwest_default(|| {
            let file = fs::File::from_std(std::fs::File::open(file_path)?);
            let reader = io::BufReader::new(file);
            let codec = codec::BytesCodec::new();
            let file_stream = codec::FramedRead::new(reader, codec)
                .map_ok(bytes::BytesMut::freeze)
                .into_stream();

            let request_builder = self
                .client
                .put(url.clone())
                .header("Content-Length", &content_length)
                .header("x-ms-blob-type", "BlockBlob")
                .body(Body::wrap_stream(file_stream));

            Ok(request_builder)
        })
        .await
        .context("BlobUploader.upload")?;

        Ok(resp)
    }

    pub async fn upload_json<D: Serialize>(
        &mut self,
        data: D,
        name: impl AsRef<str>,
    ) -> Result<Response> {
        let url = {
            let url_path = self.url.path();
            let blob_path = format!("{}/{}", url_path, name.as_ref());
            let mut url = self.url.clone();
            url.set_path(&blob_path);
            url
        };

        let resp = self
            .client
            .put(url)
            .header("x-ms-blob-type", "BlockBlob")
            .json(&data)
            .send_retry_default()
            .await
            .context("BlobUploader.upload_json")?;

        Ok(resp)
    }
}
