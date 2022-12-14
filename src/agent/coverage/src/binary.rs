// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};

use anyhow::{bail, Result};
use debuggable_module::{block, path::FilePath, Module, Offset};
use symbolic::debuginfo::Object;
use symbolic::symcache::{SymCache, SymCacheConverter};

use crate::allowlist::TargetAllowList;

#[derive(Clone, Debug, Default)]
pub struct BinaryCoverage {
    pub modules: BTreeMap<FilePath, ModuleBinaryCoverage>,
}

#[derive(Clone, Debug, Default)]
pub struct ModuleBinaryCoverage {
    pub offsets: BTreeMap<Offset, Count>,
}

impl ModuleBinaryCoverage {
    pub fn increment(&mut self, offset: Offset) -> Result<()> {
        if let Some(count) = self.offsets.get_mut(&offset) {
            count.increment();
        } else {
            bail!("unknown coverage offset: {offset:x}");
        };

        Ok(())
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Count(pub u32);

impl Count {
    pub fn increment(&mut self) {
        self.0 = self.0.saturating_add(1);
    }

    pub fn reached(&self) -> bool {
        self.0 > 0
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

    // If we wanted to apply an allowlist to function names, this is where we'd do it.
    for function in debuginfo.functions() {
        if !allowlist.functions.is_allowed(&function.name) {
            continue;
        }

        if let Some(location) = symcache.lookup(function.offset.0).next() {
            if let Some(file) = location.file() {
                let path = file.full_path();

                if allowlist.source_files.is_allowed(&path) {
                    let blocks =
                        block::sweep_region(&*module, &debuginfo, function.offset, function.size)?;
                    offsets.extend(blocks.iter().map(|b| b.offset));
                }
            }
        }
    }

    let mut coverage = ModuleBinaryCoverage::default();
    coverage
        .offsets
        .extend(offsets.into_iter().map(|o| (o, Count(0))));

    Ok(coverage)
}

impl AsRef<BTreeMap<Offset, Count>> for ModuleBinaryCoverage {
    fn as_ref(&self) -> &BTreeMap<Offset, Count> {
        &self.offsets
    }
}
