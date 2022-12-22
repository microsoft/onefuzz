// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};

use anyhow::Result;
use debuggable_module::Module;
pub use debuggable_module::{block, path::FilePath, Offset};
use symbolic::debuginfo::Object;
use symbolic::symcache::{SymCache, SymCacheConverter};

use crate::allowlist::TargetAllowList;

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct BinaryCoverage {
    pub modules: BTreeMap<FilePath, ModuleBinaryCoverage>,
}

impl BinaryCoverage {
    pub fn add(&mut self, other: &Self) {
        for (path, other_module) in &other.modules {
            let module = self.modules.entry(path.clone()).or_default();
            module.add(other_module);
        }
    }

    pub fn merge(&mut self, other: &Self) {
        for (path, other_module) in &other.modules {
            let module = self.modules.entry(path.clone()).or_default();
            module.merge(other_module);
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

    pub fn add(&mut self, other: &Self) {
        for (&offset, &other_count) in &other.offsets {
            let count = self.offsets.entry(offset).or_default();
            *count = Count::add(*count, other_count);
        }
    }

    pub fn merge(&mut self, other: &Self) {
        for (&offset, &other_count) in &other.offsets {
            let count = self.offsets.entry(offset).or_default();
            *count = Count::max(*count, other_count)
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

    pub fn add(self, other: Self)  -> Self {
        Count(self.0.saturating_add(other.0))
    }

    pub fn max(self, other: Self) -> Self {
        Count(u32::max(self.0, other.0))
    }
}

pub fn find_coverage_sites<'data>(
    module: &dyn Module<'data>,
    allowlist: &TargetAllowList,
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

    let mut offsets = BTreeSet::new();

    for function in debuginfo.functions() {
        if !allowlist.functions.is_allowed(&function.name) {
            continue;
        }

        if let Some(location) = symcache.lookup(function.offset.0).next() {
            if let Some(file) = location.file() {
                let path = file.full_path();

                if allowlist.source_files.is_allowed(path) {
                    let blocks =
                        block::sweep_region(module, &debuginfo, function.offset, function.size)?;
                    offsets.extend(blocks.iter().map(|b| b.offset));
                }
            }
        }
    }

    let coverage = ModuleBinaryCoverage::from(offsets.into_iter());

    Ok(coverage)
}

impl AsRef<BTreeMap<Offset, Count>> for ModuleBinaryCoverage {
    fn as_ref(&self) -> &BTreeMap<Offset, Count> {
        &self.offsets
    }
}

#[cfg(test)]
mod tests;
