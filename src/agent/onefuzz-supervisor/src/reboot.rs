// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;
use std::process::Command;

use anyhow::{Context, Result};
use downcast_rs::Downcast;
use tokio::fs;

use crate::work::*;

#[async_trait]
pub trait IReboot: Downcast {
    async fn save_context(&mut self, ctx: RebootContext) -> Result<()>;

    async fn load_context(&mut self) -> Result<Option<RebootContext>>;

    fn invoke(&mut self) -> Result<()>;
}

impl_downcast!(IReboot);

#[async_trait]
impl IReboot for Reboot {
    async fn save_context(&mut self, ctx: RebootContext) -> Result<()> {
        self.save_context(ctx).await
    }

    async fn load_context(&mut self) -> Result<Option<RebootContext>> {
        self.load_context().await
    }

    fn invoke(&mut self) -> Result<()> {
        self.invoke()
    }
}

pub struct Reboot;

impl Reboot {
    pub async fn save_context(&mut self, ctx: RebootContext) -> Result<()> {
        let path = reboot_context_path()?;

        info!("saving reboot context to: {}", path.display());

        let data = serde_json::to_vec(&ctx)?;
        fs::write(&path, &data)
            .await
            .with_context(|| format!("unable to save reboot context: {}", path.display()))?;

        debug!("reboot context saved");

        Ok(())
    }

    pub async fn load_context(&mut self) -> Result<Option<RebootContext>> {
        use std::io::ErrorKind;
        let path = reboot_context_path()?;

        info!("checking for saved reboot context: {}", path.display());

        let data = fs::read(&path).await;

        if let Err(err) = &data {
            if let ErrorKind::NotFound = err.kind() {
                // If new image, there won't be any reboot context.
                info!("no reboot context found");
                return Ok(None);
            }
        }

        let data = data?;
        let ctx = serde_json::from_slice(&data)?;

        fs::remove_file(&path)
            .await
            .with_context(|| format!("unable to remove reboot context: {}", path.display()))?;

        info!("loaded reboot context");
        Ok(Some(ctx))
    }

    #[cfg(target_family = "unix")]
    pub fn invoke(&mut self) -> Result<()> {
        info!("invoking local reboot command");

        Command::new("reboot").arg("-f").status()?;

        self.wait_for_reboot()
    }

    #[cfg(target_family = "windows")]
    pub fn invoke(&mut self) -> Result<()> {
        info!("invoking local reboot command");

        Command::new("powershell.exe")
            .arg("-Command")
            .arg("Restart-Computer")
            .arg("-Force")
            .status()?;

        self.wait_for_reboot()
    }

    fn wait_for_reboot(&self) -> Result<()> {
        use std::{thread, time};

        debug!("waiting for reboot");

        // 10 minutes.
        let d = time::Duration::from_secs(60 * 10);
        thread::sleep(d);

        anyhow::bail!("Failed to reboot in 10 minutes")
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct RebootContext {
    pub work_set: WorkSet,
}

impl RebootContext {
    pub fn new(work_set: WorkSet) -> Self {
        Self { work_set }
    }
}

fn reboot_context_path() -> Result<PathBuf> {
    Ok(onefuzz::fs::onefuzz_root()?.join("reboot_context.json"))
}

#[cfg(test)]
pub mod double;
