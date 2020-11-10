// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use serde::{Deserialize, Serialize};

#[cfg(target_os = "linux")]
pub mod linux;

#[cfg(target_os = "windows")]
pub mod windows;

use std::collections::BTreeMap;
use std::path::PathBuf;

/// Block coverage for a module.
///
/// May describe coverage relative to a single input or corpus of inputs.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ModuleCov {
    /// Absolute path to the module on the filesystem where coverage was recorded.
    pub module: PathBuf,

    /// Mapping from basic block module-relative offsets to block coverage info.
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

/// Coverage info for a specific block, identified by its offset.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct BlockCov {
    /// Offset of the block, relative to the module base load address.
    pub offset: u64,

    /// Size of the block in bytes.
    pub size: u32,

    /// Number of times a block was seen to be executed, relative to some input
    /// or corpus.
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
