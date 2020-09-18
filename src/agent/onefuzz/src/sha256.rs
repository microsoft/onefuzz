// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::Path;

use anyhow::Result;
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
    let data = fs::read(file).await?;

    Ok(hex::encode(Sha256::digest(&data)))
}
