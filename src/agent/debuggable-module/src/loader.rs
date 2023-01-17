// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::{Mutex, MutexGuard};

use anyhow::Result;
use thiserror::Error;

use crate::path::FilePath;

pub struct Loader {
    loaded: elsa::sync::FrozenMap<FilePath, Box<[u8]>>,
    loading: Mutex<()>,
}

impl Default for Loader {
    fn default() -> Self {
        Self {
            // sync version doesn't have a Default impl
            loaded: elsa::sync::FrozenMap::new(),
            loading: Default::default(),
        }
    }
}

impl Loader {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn load(&self, path: &FilePath) -> Result<&[u8]> {
        if let Some(data) = self.loaded.get(path) {
            return Ok(data);
        }

        // claim the lock to ensure we don't duplicate loads
        let loading_lock: MutexGuard<()> = self
            .loading
            .lock()
            .map_err(|_| LoaderError::PoisonedMutex)?;

        // re-check after claiming "loading" mutex,
        // since the data might have been loaded by someone else
        if let Some(data) = self.loaded.get(path) {
            return Ok(data);
        }

        let data: Box<[u8]> = std::fs::read(path)?.into();
        let result = self.loaded.insert(path.clone(), data);
        drop(loading_lock);
        Ok(result)
    }
}

#[derive(Error, Debug)]
pub enum LoaderError {
    #[error("internal mutex poisoned")]
    PoisonedMutex,
}
