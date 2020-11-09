// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(target_os = "linux")]
pub mod linux;

#[cfg(target_os = "windows")]
pub mod windows;

use std::collections::BTreeMap;
use std::path::PathBuf;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ModuleCov {
    pub module: PathBuf,
    pub blocks: BTreeMap<u64, BlockCov>,
}

impl ModuleCov {
    pub fn new(module: impl Into<PathBuf>, blocks: impl IntoIterator<Item = (u64, u32)>) -> Self {
        let module = module.into();
        let blocks = blocks
            .into_iter()
            .map(|(o, sz)| (o, BlockCov::new(o, sz)))
            .collect();

        Self { module, blocks }
    }
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct BlockCov {
    pub offset: u64,
    pub size: u32,
    pub count: u32,
}

impl BlockCov {
    pub fn new(offset: u64, size: u32) -> Self {
        Self {
            offset,
            size,
            count: 0,
        }
    }
}
