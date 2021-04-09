// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::Path;

use anyhow::{Context, Result};
use sha2::{Digest, Sha256};
use tokio::fs;

pub fn digest(data: impl AsRef<[u8]>) -> String {
    hex::encode(Sha256::digest(data.as_ref()))
}

pub fn digest_iter(data: impl IntoIterator<Item = impl AsRef<[u8]>>) -> String {
    let mut ctx = Sha256::new();

    for frame in data {
        ctx.update(frame);
    }

    hex::encode(ctx.finalize())
}

pub async fn digest_file(file: impl AsRef<Path>) -> Result<String> {
    let file = file.as_ref();
    let data = fs::read(file)
        .await
        .with_context(|| format!("unable to read file to generate digest: {}", file.display()))?;

    Ok(hex::encode(Sha256::digest(&data)))
}

pub fn digest_file_blocking(file: impl AsRef<Path>) -> Result<String> {
    let file = file.as_ref();
    let data = std::fs::read(file)
        .with_context(|| format!("unable to read file to generate digest: {}", file.display()))?;
    Ok(hex::encode(Sha256::digest(&data)))
}
