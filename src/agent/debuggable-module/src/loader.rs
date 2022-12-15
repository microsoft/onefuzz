// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;
use std::sync::Mutex;

use anyhow::Result;
use thiserror::Error;

use crate::path::FilePath;

#[derive(Clone, Copy)]
struct Leaked(&'static [u8]);

impl Leaked {
    pub fn into_raw(self) -> *mut [u8] {
        self.0 as *const _ as *mut _
    }
}

impl From<Vec<u8>> for Leaked {
    fn from(data: Vec<u8>) -> Self {
        let data = Box::leak(data.into_boxed_slice());
        Leaked(data)
    }
}

#[derive(Default)]
pub struct Loader {
    loaded: Mutex<HashMap<FilePath, Leaked>>,
}

impl Loader {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn load(&self, path: &FilePath) -> Result<&[u8]> {
        if let Some(data) = self.get(path)? {
            Ok(data)
        } else {
            self.load_new(path)
        }
    }

    fn load_new(&self, path: &FilePath) -> Result<&[u8]> {
        let mut loaded = self.loaded.lock().map_err(|_| LoaderError::PoisonedMutex)?;
        let data = std::fs::read(path)?;
        let leaked = Leaked::from(data);
        loaded.insert(path.clone(), leaked);

        Ok(leaked.0)
    }

    pub fn get(&self, path: &FilePath) -> Result<Option<&[u8]>> {
        let loaded = self.loaded.lock().map_err(|_| LoaderError::PoisonedMutex)?;

        let data = loaded.get(path).map(|l| l.0);

        Ok(data)
    }
}

impl Drop for Loader {
    fn drop(&mut self) {
        if let Ok(mut loaded) = self.loaded.lock() {
            for (_, leaked) in loaded.drain() {
                unsafe {
                    let raw = leaked.into_raw();
                    let owned = Box::from_raw(raw);
                    drop(owned);
                }
            }

            debug_assert!(loaded.is_empty());
        }
    }
}

#[derive(Error, Debug)]
pub enum LoaderError {
    #[error("internal mutex poisoned")]
    PoisonedMutex,
}
