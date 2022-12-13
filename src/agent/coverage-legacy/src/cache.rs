// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeSet, HashMap};

#[cfg(any(target_os = "windows", target_os = "linux"))]
use anyhow::Result;
use serde::{Deserialize, Serialize};

#[cfg(target_os = "windows")]
use winapi::um::winnt::HANDLE;

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

    #[cfg(target_os = "linux")]
    pub fn fetch(&mut self, path: &ModulePath) -> Result<Option<&ModuleInfo>> {
        if !self.cached.contains_key(path) {
            self.insert(path)?;
        }

        Ok(self.cached.get(path))
    }

    #[cfg(target_os = "windows")]
    pub fn fetch(
        &mut self,
        path: &ModulePath,
        handle: impl Into<Option<HANDLE>>,
    ) -> Result<Option<&ModuleInfo>> {
        if !self.cached.contains_key(path) {
            self.insert(path, handle)?;
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
    pub fn insert(&mut self, path: &ModulePath, handle: impl Into<Option<HANDLE>>) -> Result<()> {
        let entry = ModuleInfo::new_pe(path, handle)?;
        self.cached.insert(path.clone(), entry);
        Ok(())
    }
}

#[derive(Clone, Debug, Deserialize, Serialize)]
pub struct ModuleInfo {
    /// Index of the module segments and symbol metadata.
    pub module: ModuleIndex,

    /// Set of image offsets of basic blocks.
    pub blocks: BTreeSet<u32>,
}

impl ModuleInfo {
    #[cfg(target_os = "linux")]
    pub fn new_elf(path: &ModulePath) -> Result<Self> {
        use crate::elf::{ElfContext, ElfSancovBasicBlockProvider};

        let data = std::fs::read(path)?;
        let elf = goblin::elf::Elf::parse(&data)?;
        let module = ModuleIndex::index_elf(path.clone(), &elf)?;

        let ctx = ElfContext::new(&data, &elf)?;
        let mut sancov_provider = ElfSancovBasicBlockProvider::new(ctx);
        let blocks = if let Ok(blocks) = sancov_provider.provide() {
            blocks
        } else {
            let disasm = crate::disasm::ModuleDisassembler::new(&module, &data)?;
            disasm.find_blocks()
        };

        Ok(Self { module, blocks })
    }

    #[cfg(target_os = "windows")]
    pub fn new_pe(path: &ModulePath, handle: impl Into<Option<HANDLE>>) -> Result<Self> {
        use crate::block::pe_provider::PeSancovBasicBlockProvider;

        let handle = handle.into();

        let file = std::fs::File::open(path)?;
        let data = unsafe { memmap2::Mmap::map(&file)? };

        let pe = goblin::pe::PE::parse(&data)?;
        let module = ModuleIndex::index_pe(path.clone(), &pe);

        let pdb_path = crate::pdb::find_pdb_path(path.as_ref(), &pe, handle)?
            .ok_or_else(|| anyhow::format_err!("could not find PDB for module: {}", path))?;

        let pdb = std::fs::File::open(&pdb_path)?;
        let mut pdb = pdb::PDB::open(pdb)?;

        let mut sancov_provider = PeSancovBasicBlockProvider::new(&data, &pe, &mut pdb);

        let blocks = if let Ok(blocks) = sancov_provider.provide() {
            blocks
        } else {
            let bitset = crate::pe::process_module(path, &data, &pe, false, handle)?;
            bitset.ones().map(|off| off as u32).collect()
        };

        Ok(Self { module, blocks })
    }
}
