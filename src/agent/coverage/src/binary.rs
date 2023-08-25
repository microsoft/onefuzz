// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use anyhow::{bail, Result};
use debuggable_module::block::Blocks;
use debuggable_module::Module;
pub use debuggable_module::{block, path::FilePath, Offset};
use symbolic::debuginfo::Object;
use symbolic::symcache::{SymCache, SymCacheConverter};

use crate::allowlist::AllowList;

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct BinaryCoverage {
    pub modules: BTreeMap<FilePath, ModuleBinaryCoverage>,
}

impl BinaryCoverage {
    pub fn add(&mut self, rhs: &Self) {
        for (path, rhs_module) in &rhs.modules {
            let module = self.modules.entry(path.clone()).or_default();
            module.add(rhs_module);
        }
    }

    pub fn merge(&mut self, rhs: &Self) {
        for (path, rhs_module) in &rhs.modules {
            let module = self.modules.entry(path.clone()).or_default();
            module.merge(rhs_module);
        }
    }
}

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct ModuleBinaryCoverage {
    pub offsets: BTreeMap<Offset, Count>,
}

impl ModuleBinaryCoverage {
    pub fn increment(&mut self, offset: Offset) {
        let count = self.offsets.entry(offset).or_default();
        count.increment();
    }

    pub fn add(&mut self, rhs: &Self) {
        for (&offset, &rhs_count) in &rhs.offsets {
            let count = self.offsets.entry(offset).or_default();
            *count += rhs_count;
        }
    }

    pub fn merge(&mut self, rhs: &Self) {
        for (&offset, &rhs_count) in &rhs.offsets {
            let count = self.offsets.entry(offset).or_default();
            *count = Count::max(*count, rhs_count)
        }
    }
}

impl<O> From<O> for ModuleBinaryCoverage
where
    O: IntoIterator<Item = Offset>,
{
    fn from(offsets: O) -> Self {
        let offsets = offsets.into_iter().map(|o| (o, Count(0)));

        let mut coverage = Self::default();
        coverage.offsets.extend(offsets);
        coverage
    }
}

#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct Count(pub u32);

impl Count {
    pub fn increment(&mut self) {
        self.0 = self.0.saturating_add(1);
    }

    pub fn reached(&self) -> bool {
        self.0 > 0
    }

    pub fn max(self, rhs: Self) -> Self {
        Count(u32::max(self.0, rhs.0))
    }
}

impl std::ops::Add for Count {
    type Output = Self;

    fn add(self, rhs: Self) -> Self {
        Count(self.0.saturating_add(rhs.0))
    }
}

impl std::ops::AddAssign for Count {
    fn add_assign(&mut self, rhs: Self) {
        *self = *self + rhs;
    }
}

/// Cache of analyzed binary metadata for a set of modules, relative to a common
/// source allowlist.
pub struct DebugInfoCache {
    allowlist: Arc<AllowList>,
    modules: Arc<Mutex<BTreeMap<FilePath, CachedDebugInfo>>>,
}

impl DebugInfoCache {
    pub fn new(allowlist: AllowList) -> Self {
        let allowlist = Arc::new(allowlist);
        let modules = Arc::new(Mutex::new(BTreeMap::new()));

        Self { allowlist, modules }
    }

    pub fn get_or_insert(&self, module: &dyn Module) -> Result<CachedDebugInfo> {
        if !self.is_cached(module) {
            self.insert(module)?;
        }

        if let Some(cached) = self.get(module.executable_path()) {
            Ok(cached)
        } else {
            // Unreachable.
            bail!("module should be cached but data is missing")
        }
    }

    fn get(&self, path: &FilePath) -> Option<CachedDebugInfo> {
        self.modules.lock().unwrap().get(path).cloned()
    }

    fn insert(&self, module: &dyn Module) -> Result<()> {
        let debuginfo = module.debuginfo()?;

        let mut symcache = vec![];
        let mut converter = SymCacheConverter::new();
        let exe = Object::parse(module.executable_data())?;
        converter.process_object(&exe)?;
        let di = Object::parse(module.debuginfo_data())?;
        converter.process_object(&di)?;
        converter.serialize(&mut std::io::Cursor::new(&mut symcache))?;
        let symcache = SymCache::parse(&symcache)?;

        let mut blocks = Blocks::new();

        for function in debuginfo.functions() {
            if let Some(location) = symcache.lookup(function.offset.0).next() {
                if let Some(file) = location.file() {
                    if !self.allowlist.is_allowed(file.full_path()) {
                        debug!(
                            "skipping sweep of `{}:{}` due to excluded source path `{}`",
                            module.executable_path(),
                            function.name,
                            file.full_path(),
                        );
                        continue;
                    }
                }
            }

            let fn_blocks =
                block::sweep_region(module, &debuginfo, function.offset, function.size)?;

            for block in &fn_blocks {
                if let Some(location) = symcache.lookup(block.offset.0).next() {
                    if let Some(file) = location.file() {
                        let path = file.full_path();

                        // Apply allowlists per block, to account for inlining. The `location` values
                        // here describe the top of the inline-inclusive call stack.
                        if !self.allowlist.is_allowed(path) {
                            continue;
                        }

                        blocks.map.insert(block.offset, *block);
                    }
                }
            }
        }

        let coverage = ModuleBinaryCoverage::from((&blocks).into_iter().map(|b| b.offset));
        let cached = CachedDebugInfo::new(blocks, coverage);
        self.modules
            .lock()
            .unwrap()
            .insert(module.executable_path().clone(), cached);

        Ok(())
    }

    fn is_cached(&self, module: &dyn Module) -> bool {
        self.get(module.executable_path()).is_some()
    }
}

#[derive(Clone, Debug)]
pub struct CachedDebugInfo {
    pub blocks: Blocks,
    pub coverage: ModuleBinaryCoverage,
}

impl CachedDebugInfo {
    pub fn new(blocks: Blocks, coverage: ModuleBinaryCoverage) -> Self {
        Self { blocks, coverage }
    }
}

pub fn find_coverage_sites(
    module: &dyn Module,
    source_allowlist: &AllowList,
) -> Result<ModuleBinaryCoverage> {
    let debuginfo = module.debuginfo()?;

    let mut symcache = vec![];
    let mut converter = SymCacheConverter::new();
    let exe = Object::parse(module.executable_data())?;
    converter.process_object(&exe)?;
    let di = Object::parse(module.debuginfo_data())?;
    converter.process_object(&di)?;
    converter.serialize(&mut std::io::Cursor::new(&mut symcache))?;
    let symcache = SymCache::parse(&symcache)?;

    let mut blocks = Blocks::new();

    for function in debuginfo.functions() {
        if let Some(location) = symcache.lookup(function.offset.0).next() {
            if let Some(file) = location.file() {
                if !source_allowlist.is_allowed(file.full_path()) {
                    debug!(
                        "skipping sweep of `{}:{}` due to excluded source path `{}`",
                        module.executable_path(),
                        function.name,
                        file.full_path(),
                    );
                    continue;
                }
            }
        }

        let fn_blocks = block::sweep_region(module, &debuginfo, function.offset, function.size)?;

        for block in &fn_blocks {
            if let Some(location) = symcache.lookup(block.offset.0).next() {
                if let Some(file) = location.file() {
                    let path = file.full_path();

                    // Apply allowlists per block, to account for inlining. The `location` values
                    // here describe the top of the inline-inclusive call stack.
                    if !source_allowlist.is_allowed(path) {
                        continue;
                    }

                    blocks.map.insert(block.offset, *block);
                }
            }
        }
    }

    let coverage = ModuleBinaryCoverage::from((&blocks).into_iter().map(|b| b.offset));

    Ok(coverage)
}

impl AsRef<BTreeMap<Offset, Count>> for ModuleBinaryCoverage {
    fn as_ref(&self) -> &BTreeMap<Offset, Count> {
        &self.offsets
    }
}

#[cfg(test)]
mod tests;
