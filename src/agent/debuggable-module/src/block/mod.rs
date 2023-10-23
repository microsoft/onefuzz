// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::collections::BTreeMap;

use crate::debuginfo::DebugInfo;
use crate::{Module, Offset};

#[cfg(target_arch = "aarch64")]
pub use self::arm64::sweep_region;
#[cfg(target_arch = "x86_64")]
pub use self::x86_64::sweep_region;

pub mod arm64;
pub mod x86_64;

pub fn sweep_module(module: &dyn Module, debuginfo: &DebugInfo) -> Result<Blocks> {
    let mut blocks = Blocks::default();

    for function in debuginfo.functions() {
        let function_blocks = sweep_region(module, debuginfo, function.offset, function.size)?;
        blocks.map.extend(&function_blocks.map);
    }

    Ok(blocks)
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Block {
    pub offset: Offset,
    pub size: u64,
}

impl Block {
    pub fn new(offset: Offset, size: u64) -> Self {
        Self { offset, size }
    }

    pub fn contains(&self, offset: &Offset) -> bool {
        self.offset.region(self.size).contains(&offset.0)
    }
}

#[derive(Clone, Debug, Default)]
pub struct Blocks {
    pub map: BTreeMap<Offset, Block>,
}

impl Blocks {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn iter(&self) -> impl Iterator<Item = &Block> {
        self.map.values()
    }

    pub fn find(&self, offset: &Offset) -> Option<&Block> {
        self.map.values().find(|b| b.contains(offset))
    }

    pub fn extend<'b>(&mut self, blocks: impl IntoIterator<Item = &'b Block>) {
        for &b in blocks.into_iter() {
            self.map.insert(b.offset, b);
        }
    }
}

impl<'b> IntoIterator for &'b Blocks {
    type Item = &'b Block;
    type IntoIter = std::collections::btree_map::Values<'b, Offset, Block>;

    fn into_iter(self) -> Self::IntoIter {
        self.map.values()
    }
}
