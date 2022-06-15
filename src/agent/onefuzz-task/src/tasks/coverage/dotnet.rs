use std::{path::PathBuf, collections::HashMap};

use onefuzz::syncdir::SyncedDir;
use anyhow::{Context, Result};
use storage_queue::{Message, QueueClient};

use crate::tasks::{generic::input_poller::InputPoller, config::CommonConfig};

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct CoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("dotnet_coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting dotnet_coverage task");
        self.config.coverage.init_pull().await?;
        Ok(())
    }
}
