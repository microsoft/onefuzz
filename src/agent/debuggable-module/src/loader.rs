// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;

use crate::path::FilePath;

pub struct Loader {
    loaded: elsa::sync::FrozenMap<FilePath, Box<[u8]>>,
}

impl Loader {
    pub fn new() -> Self {
        Loader {
            loaded: elsa::sync::FrozenMap::new(),
        }
    }

    pub fn load(&self, path: &FilePath) -> Result<&[u8]> {
        if let Some(data) = self.loaded.get(path) {
            return Ok(data);
        }

        let data: Box<[u8]> = std::fs::read(path)?.into();
        Ok(self.loaded.insert(path.clone(), data))
    }
}
