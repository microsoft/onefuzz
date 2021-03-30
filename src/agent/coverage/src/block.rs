// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(target_os = "linux")]
pub mod linux;

#[cfg(target_os = "windows")]
pub mod windows;

use std::collections::BTreeMap;

use anyhow::Result;
use serde::{Deserialize, Serialize};

use crate::code::ModulePath;

/// Block coverage for a command invocation.
///
/// Organized by module.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct CommandBlockCov {
    modules: BTreeMap<ModulePath, ModuleCov>,
}

impl CommandBlockCov {
    /// Returns `true` if the module was newly-inserted (which initializes its
    /// block coverage map). Otherwise, returns `false`, and no re-computation
    /// is performed.
    pub fn insert(&mut self, path: &ModulePath, offsets: impl Iterator<Item = u64>) -> bool {
        use std::collections::btree_map::Entry;

        match self.modules.entry(path.clone()) {
            Entry::Occupied(_entry) => false,
            Entry::Vacant(entry) => {
                entry.insert(ModuleCov::new(offsets));
                true
            }
        }
    }

    pub fn increment(&mut self, path: &ModulePath, offset: u64) -> Result<()> {
        if let Some(module) = self.modules.get_mut(path) {
            module.increment(offset);
        } else {
            log::error!(
                "missing module when incrementing coverage at {}+{:x}",
                path,
                offset
            );
        }

        Ok(())
    }

    pub fn iter(&self) -> impl Iterator<Item = (&ModulePath, &ModuleCov)> {
        self.modules.iter()
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ModuleCov {
    pub blocks: BTreeMap<u64, BlockCov>,
}

impl ModuleCov {
    pub fn new(offsets: impl Iterator<Item = u64>) -> Self {
        let blocks = offsets.map(|o| (o, BlockCov::new(o))).collect();
        Self { blocks }
    }

    pub fn increment(&mut self, offset: u64) {
        if let Some(block) = self.blocks.get_mut(&offset) {
            block.count += 1;
        }
    }
}

/// Coverage info for a specific block, identified by its offset.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct BlockCov {
    /// Offset of the block, relative to the module base load address.
    pub offset: u64,

    /// Number of times a block was seen to be executed, relative to some input
    /// or corpus.
    ///
    /// Right now, we only set one-shot breakpoints, so the max `count` for a
    /// single input is 1. In this usage, if we measure corpus block coverage
    /// with `sum()` as the aggregation function, then `count` / `corpus.len()`
    /// tells us the proportion of corpus inputs that cover a block.
    ///
    /// If we reset breakpoints and recorded multiple block hits per input, then
    /// the corpus semantics would depend on the aggregation function.
    pub count: u32,
}

impl BlockCov {
    pub fn new(offset: u64) -> Self {
        Self { offset, count: 0 }
    }
}
