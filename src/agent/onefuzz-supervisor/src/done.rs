// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fs::metadata;
use std::path::PathBuf;

use anyhow::{Context, Result};
use onefuzz::fs::onefuzz_root;
use tokio::fs;

pub async fn set_done_lock() -> Result<()> {
    let path = done_path()?;
    fs::write(&path, "")
        .await
        .with_context(|| format!("unable to write done lock: {}", path.display()))?;
    Ok(())
}

pub fn is_agent_done() -> Result<bool> {
    Ok(metadata(done_path()?).is_ok())
}

pub fn done_path() -> Result<PathBuf> {
    Ok(onefuzz_root()?.join("supervisor-is-done"))
}
