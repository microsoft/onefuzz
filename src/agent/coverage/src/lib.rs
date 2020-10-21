// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![cfg(windows)]
#![allow(clippy::as_conversions)]
#![allow(clippy::new_without_default)]

mod intel;
pub mod pe;

pub mod block;

use std::{
    ffi::OsString,
    fs::File,
    path::{Path, PathBuf},
};

use anyhow::Result;
use fixedbitset::FixedBitSet;
use serde::{Deserialize, Serialize};

pub const COVERAGE_MAP: &str = "coverage-map";

#[derive(Deserialize, Serialize, Debug, Clone)]
pub struct Block {
    rva: u32,
    hit: bool,
}

impl Block {
    pub fn new(rva: u32, hit: bool) -> Self {
        Block { rva, hit }
    }

    pub fn rva(&self) -> u32 {
        self.rva
    }

    pub fn hit(&self) -> bool {
        self.hit
    }

    pub fn set_hit(&mut self) {
        self.hit = true;
    }
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct ModuleCoverageBlocks {
    module: OsString,
    path: PathBuf,
    blocks: Vec<Block>,
}

impl ModuleCoverageBlocks {
    pub fn new(
        path: impl Into<PathBuf>,
        module: impl Into<OsString>,
        rvas_bitset: FixedBitSet,
    ) -> Self {
        let blocks: Vec<_> = rvas_bitset
            .ones()
            .map(|rva| Block::new(rva as u32, false))
            .collect();

        ModuleCoverageBlocks {
            path: path.into(),
            module: module.into(),
            blocks,
        }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn name(&self) -> &Path {
        Path::new(&self.module)
    }

    pub fn blocks(&self) -> &[Block] {
        &self.blocks
    }

    pub fn set_block_hit(&mut self, block_index: usize) {
        self.blocks[block_index].set_hit();
    }

    pub fn count_blocks_hit(&self) -> usize {
        self.blocks.iter().filter(|b| b.hit).count()
    }
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct AppCoverageBlocks {
    modules: Vec<ModuleCoverageBlocks>,
}

impl AppCoverageBlocks {
    pub fn new() -> Self {
        let modules = vec![];
        Self { modules }
    }

    pub fn modules(&self) -> &[ModuleCoverageBlocks] {
        &self.modules
    }

    pub fn add_module(&mut self, module: ModuleCoverageBlocks) -> usize {
        let idx = self.modules.len();
        self.modules.push(module);
        idx
    }

    pub fn report_block_hit(&mut self, module_index: usize, block_index: usize) {
        self.modules[module_index].set_block_hit(block_index);
    }

    pub fn save(&self, path: impl AsRef<Path>) -> Result<()> {
        let cov_file = File::create(path)?;
        bincode::serialize_into(&cov_file, self)?;
        Ok(())
    }

    pub fn count_blocks(&self) -> usize {
        self.modules().iter().map(|m| m.blocks().len()).sum()
    }

    pub fn count_blocks_hit(&self) -> usize {
        self.modules().iter().map(|m| m.count_blocks_hit()).sum()
    }
}

/// Statically analyze the specified images to discover the basic block
/// entry points and write out the results in a file in `output_dir`.
pub fn run_init(output_dir: PathBuf, modules: Vec<PathBuf>, function: bool) -> Result<()> {
    let mut result = AppCoverageBlocks::new();
    for module in modules {
        if module.is_file() {
            let rvas_bitset = pe::process_image(&module, function)?;

            let module_name = module.file_stem().unwrap(); // Unwrap guaranteed by `is_file` test above.
            let module_rvas = ModuleCoverageBlocks::new(module.clone(), &module_name, rvas_bitset);
            result.add_module(module_rvas);
        } else {
            anyhow::bail!("Cannot find file `{}`", module.as_path().display());
        }
    }

    let output_file = output_dir.join(COVERAGE_MAP);
    result.save(&output_file)?;

    Ok(())
}

/// Load a coverage map created by `run_init`.
pub fn load_coverage_map(output_dir: &Path) -> Result<Option<AppCoverageBlocks>> {
    if let Ok(cov_file) = File::open(output_dir.join(COVERAGE_MAP)) {
        Ok(Some(bincode::deserialize_from(cov_file)?))
    } else {
        Ok(None)
    }
}
