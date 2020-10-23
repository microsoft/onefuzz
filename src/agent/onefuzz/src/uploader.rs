// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::Path;

use anyhow::Result;
use futures::stream::TryStreamExt;
use reqwest as r;
use reqwest_retry::SendRetry;
use serde::Serialize;
use tokio::{fs, io};
use tokio_util::codec;

#[derive(Clone)]
pub struct BlobUploader {
    client: r::Client,
    url: r::Url,
}

impl BlobUploader {
    pub fn new(url: r::Url) -> Self {
        let client = r::Client::new();

        Self { client, url }
    }

    pub async fn upload(&mut self, file_path: impl AsRef<Path>) -> Result<r::Response> {
        let file_path = file_path.as_ref();

        let file_name = file_path.file_name().unwrap().to_str().unwrap();

        let metadata = fs::metadata(file_path).await?;
        let file_len = metadata.len();

        let file = fs::File::open(file_path).await?;
        let reader = io::BufReader::new(file);
        let codec = codec::BytesCodec::new();
        let file_stream = codec::FramedRead::new(reader, codec).map_ok(bytes::BytesMut::freeze);

        let body = r::Body::wrap_stream(file_stream);

        let url = {
            let url_path = self.url.path();
            let blob_path = format!("{}/{}", url_path, file_name);
            let mut url = self.url.clone();
            url.set_path(&blob_path);
            url
        };

        // Check if the file already exists before uploading
        let head = self.client.head(url.clone()).send_retry_default().await?;
        if head.status() == reqwest::StatusCode::OK {
            return Ok(head);
        }

        let content_length = format!("{}", file_len);

        let resp = self
            .client
            .put(url)
            .header("Content-Length", &content_length)
            .header("x-ms-blob-type", "BlockBlob")
            .body(body)
            .send_retry_default()
            .await?;

        Ok(resp)
    }

    pub async fn upload_json<D: Serialize>(
        &mut self,
        data: D,
        name: impl AsRef<str>,
    ) -> Result<r::Response> {
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
            .await?;

        Ok(resp)
    }
}
