// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};

use anyhow::Result;
use tokio::{fs, io};

pub struct TotalCoverage {
    /// Absolute path to the total coverage file.
    ///
    /// May not yet exist on disk.
    path: PathBuf,
}

#[derive(Debug)]
pub struct Info {
    pub covered: u64,
    pub features: u64,
    pub rate: f64,
}

impl TotalCoverage {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub async fn data(&self) -> Result<Option<Vec<u8>>> {
        use io::ErrorKind::NotFound;

        let data = fs::read(&self.path).await;

        if let Err(err) = &data {
            if err.kind() == NotFound {
                return Ok(None);
            }
        }

        Ok(Some(data?))
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub async fn write(&self, data: &[u8]) -> Result<()> {
        fs::write(self.path(), data).await?;
        Ok(())
    }

    pub async fn update_bytes(&self, new_data: &[u8]) -> Result<()> {
        match self.data().await {
            Ok(Some(mut total_data)) => {
                if total_data.len() < new_data.len() {
                    total_data.resize_with(new_data.len(), || 0);
                }
                for (i, b) in new_data.iter().enumerate() {
                    if *b > 0 {
                        total_data[i] = 1;
                    }
                }
                self.write(&total_data).await?;
            }
            Ok(None) => {
                // Base case: we don't yet have any total coverage. Promote the
                // new coverage to being our total coverage.
                info!("initializing total coverage map {}", self.path().display());
                self.write(new_data).await?;
            }
            Err(err) => {
                // Couldn't read total for some other reason, so this is a real error.
                return Err(err);
            }
        }

        Ok(())
    }

    pub async fn info(&self) -> Result<Info> {
        let data = self
            .data()
            .await?
            .ok_or_else(|| format_err!("coverage file not found"))?;

        let covered = data.iter().filter(|&&c| c > 0).count() as u64;
        let features = data.len() as u64;
        let rate = (covered as f64) / (features as f64);
        Ok(Info {
            covered,
            features,
            rate,
        })
    }
}
