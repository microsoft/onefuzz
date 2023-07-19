// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fs::metadata;
use std::path::PathBuf;

use anyhow::{Context, Result};
use onefuzz::fs::onefuzz_root;
use tokio::fs;
use uuid::Uuid;

pub async fn set_done_lock(machine_id: Uuid) -> Result<()> {
    let path = done_path(machine_id)?;
    fs::write(&path, "")
        .await
        .with_context(|| format!("unable to write done lock: {}", path.display()))?;
    Ok(())
}

pub fn remove_done_lock(machine_id: Uuid) -> Result<()> {
    let path = done_path(machine_id)?;
    if path.exists() {
        std::fs::remove_file(&path)
            .with_context(|| format!("unable to remove done lock: {}", path.display()))?;
    }

    Ok(())
}

pub fn is_agent_done(machine_id: Uuid) -> Result<bool> {
    Ok(metadata(done_path(machine_id)?).is_ok())
}

pub fn done_path(machine_id: Uuid) -> Result<PathBuf> {
    Ok(onefuzz_root()?.join(format!("supervisor-is-done-{machine_id}")))
}
