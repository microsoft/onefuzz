// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;

use crate::path::FilePath;

#[derive(Default)]
pub struct Loader {
    loaded: elsa::sync::FrozenMap<FilePath, Box<[u8]>>,
}

impl Loader {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn load(&self, path: &FilePath) -> Result<&[u8]> {
        // Note: if we ever have this callable in parallel from
        //       multiple threads, we should use some kind of
        //       lock to prevent loading the same file multiple times.

        if let Some(data) = self.loaded.get(path) {
            return Ok(data);
        }

        let data: Box<[u8]> = std::fs::read(path)?.into();
        Ok(self.loaded.insert(path.clone(), data))
    }
}
