// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{HashMap, BTreeSet};
use anyhow::Result;
use serde::{Deserialize, Serialize};

use crate::code::{ModuleIndex, ModulePath};
use crate::disasm::ModuleDisassembler;


#[derive(Clone, Debug, Default, Deserialize, Serialize)]
pub struct ModuleCache {
    pub cached: HashMap<ModulePath, ModuleInfo>,
}

impl ModuleCache {
    pub fn new() -> Self {
        let cached = HashMap::new();

        Self { cached }
    }

    pub fn fetch(&mut self, path: &ModulePath) -> Result<Option<&ModuleInfo>> {
        if !self.cached.contains_key(path) {
            self.insert(path)?;
        }

        Ok(self.cached.get(path))
    }

    pub fn insert(&mut self, path: &ModulePath) -> Result<()> {
        let entry = ModuleInfo::new_elf(path)?;
        self.cached.insert(path.clone(), entry);
        Ok(())
    }
}

#[derive(Clone, Debug, Deserialize, Serialize)]
pub struct ModuleInfo {
    /// Index of the module segments and symbol metadata.
    pub module: ModuleIndex,

    /// Set of image offsets of basic blocks.
    pub blocks: BTreeSet<u64>,
}

impl ModuleInfo {
    pub fn new_elf(path: &ModulePath) -> Result<Self> {
        let data = std::fs::read(path)?;
        let module = ModuleIndex::parse_elf(path.clone(), &data)?;
        let disasm = ModuleDisassembler::new(&module, &data)?;
        let blocks = disasm.find_blocks();

        Ok(Self { module, blocks })
    }
}