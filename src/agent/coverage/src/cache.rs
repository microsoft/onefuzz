// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeSet, HashMap};

use anyhow::Result;
use serde::{Deserialize, Serialize};

use crate::code::{ModuleIndex, ModulePath};

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

    #[cfg(target_os = "linux")]
    pub fn insert(&mut self, path: &ModulePath) -> Result<()> {
        let entry = ModuleInfo::new_elf(path)?;
        self.cached.insert(path.clone(), entry);
        Ok(())
    }

    #[cfg(target_os = "windows")]
    pub fn insert(&mut self, path: &ModulePath) -> Result<()> {
        let entry = ModuleInfo::new_pe(path)?;
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
    #[cfg(target_os = "linux")]
    pub fn new_elf(path: &ModulePath) -> Result<Self> {
        let data = std::fs::read(path)?;
        let elf = goblin::elf::Elf::parse(&data)?;
        let module = ModuleIndex::index_elf(path.clone(), &elf)?;
        let disasm = crate::disasm::ModuleDisassembler::new(&module, &data)?;
        let blocks = disasm.find_blocks();

        Ok(Self { module, blocks })
    }

    #[cfg(target_os = "windows")]
    pub fn new_pe(path: &ModulePath) -> Result<Self> {
        let file = std::fs::File::open(path)?;
        let data = unsafe { memmap2::Mmap::map(&file)? };

        let pe = goblin::pe::PE::parse(&data)?;
        let module = ModuleIndex::index_pe(path.clone(), &pe);
        let offsets = crate::pe::process_module(path, &data, &pe, false)?;
        let blocks = offsets.ones().map(|off| off as u64).collect();

        Ok(Self { module, blocks })
    }
}
