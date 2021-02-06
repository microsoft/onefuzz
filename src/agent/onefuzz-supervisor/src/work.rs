// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::io::ErrorKind;
use std::path::PathBuf;

use anyhow::{Context, Result};
use downcast_rs::Downcast;
use onefuzz::{blob::BlobContainerUrl, http::is_auth_error};
use serde::de::DeserializeOwned;
use storage_queue::{Message as QueueMessage, QueueClient};
use tokio::fs;
use uuid::Uuid;

use crate::auth::Secret;
use crate::config::Registration;

pub type JobId = Uuid;

pub type TaskId = Uuid;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct WorkSet {
    pub reboot: bool,
    pub setup_url: BlobContainerUrl,
    pub script: bool,
    pub work_units: Vec<WorkUnit>,
}

impl WorkSet {
    pub fn task_ids(&self) -> Vec<TaskId> {
        self.work_units.iter().map(|w| w.task_id).collect()
    }

    pub fn context_path() -> Result<PathBuf> {
        Ok(onefuzz::fs::onefuzz_root()?.join("workset_context.json"))
    }

    pub async fn load_from_fs_context() -> Result<Option<Self>> {
        let path = Self::context_path()?;

        info!("checking for workset context: {}", path.display());

        let data = fs::read(path).await;

        if let Err(err) = &data {
            if let ErrorKind::NotFound = err.kind() {
                // If new image, there won't be any reboot context.
                info!("no workset context found, assuming first launch");
                return Ok(None);
            }
        }

        let data = data?;
        let ctx = serde_json::from_slice(&data)?;

        info!("loaded workset context");

        Ok(Some(ctx))
    }

    pub async fn save_context(&self) -> Result<()> {
        let path = Self::context_path()?;
        info!("saving workset context: {}", path.display());

        let data = serde_json::to_vec(&self)?;
        fs::write(&path, &data)
            .await
            .with_context(|| format!("unable to save WorkSet context: {}", path.display()))?;

        Ok(())
    }

    pub fn setup_dir(&self) -> Result<PathBuf> {
        let setup_dir = self.setup_url.container();
        Ok(onefuzz::fs::onefuzz_root()?
            .join("blob-containers")
            .join(setup_dir))
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct WorkUnit {
    /// Job that the work is part of.
    pub job_id: JobId,

    /// Task that the work is part of.
    pub task_id: TaskId,

    /// JSON-serialized task config.
    pub config: Secret<String>,
}

impl WorkUnit {
    pub fn working_dir(&self) -> Result<PathBuf> {
        Ok(onefuzz::fs::onefuzz_root()?.join(self.task_id.to_string()))
    }

    pub fn config_path(&self) -> Result<PathBuf> {
        Ok(self.working_dir()?.join("config.json"))
    }
}

#[async_trait]
pub trait IWorkQueue: Downcast {
    async fn poll(&mut self) -> Result<Option<Message>>;

    //async fn claim(&mut self, receipt: Receipt) -> Result<()>;
}

#[async_trait]
impl IWorkQueue for WorkQueue {
    async fn poll(&mut self) -> Result<Option<Message>> {
        self.poll().await
    }

    // async fn claim(&mut self, receipt: Receipt) -> Result<()> {
    //     self.claim(receipt).await
    // }
}

impl_downcast!(IWorkQueue);

pub struct Message {
    pub queue_message: QueueMessage,
    pub work_set: WorkSet,
}

impl Message {
    pub async fn claim(self) -> Result<WorkSet> {
        let result = self.queue_message.claim().await?;
        Ok(result)

        // todo: handle token renewal
        // // If we had an auth err, renew our registration and retry once, in case
        // // it was just due to a stale SAS URL.
        // if let Err(err) = &result {
        //     if is_auth_error(err) {
        //         self.renew().await?;
        //         self.queue.delete(receipt).await?;
        //     }
        // }
    }
}

// #[derive(Clone, Debug, Eq, PartialEq)]
// pub struct Receipt(pub storage_queue::Receipt);

pub struct WorkQueue {
    queue: QueueClient,
    registration: Registration,
}

impl WorkQueue {
    pub fn new(registration: Registration) -> Self {
        let url = registration.dynamic_config.work_queue.clone();
        let queue = QueueClient::new(url);

        Self {
            queue,
            registration,
        }
    }

    async fn renew(&mut self) -> Result<()> {
        self.registration.renew().await?;
        let url = self.registration.dynamic_config.work_queue.clone();
        self.queue = QueueClient::new(url);
        Ok(())
    }

    pub async fn poll(&mut self) -> Result<Option<Message>> {
        let msg = self.queue.pop().await;

        // // If we had an auth err, renew our registration and retry once, in case
        // // it was just due to a stale SAS URL.
        // if let Err(err) = &msg {
        //     if is_auth_error(err) {
        //         self.renew().await?;
        //         msg = self.queue.pop().await;
        //     }
        // }

        // Now we've had a chance to ensure our SAS URL is fresh. For any other
        // error, including another auth error, bail.
        let msg = msg?;

        if msg.is_none() {
            return Ok(None);
        }

        let queue_message = msg.unwrap();
        let work_set: WorkSet = queue_message.get()?;
        // let receipt = Receipt(msg.);
        let msg = Message {
            queue_message,
            work_set,
        };

        Ok(Some(msg))
    }

    // pub async fn claim(&mut self, receipt: Receipt) -> Result<()> {
    //     let receipt = receipt.0;

    //     let result = self.queue.delete(receipt.clone()).await;

    //     // If we had an auth err, renew our registration and retry once, in case
    //     // it was just due to a stale SAS URL.
    //     if let Err(err) = &result {
    //         if is_auth_error(err) {
    //             self.renew().await?;
    //             self.queue.delete(receipt).await?;
    //         }
    //     }

    //     Ok(())
    // }
}

#[cfg(test)]
pub mod double;
