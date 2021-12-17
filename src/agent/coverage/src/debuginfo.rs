// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{HashMap, HashSet};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use anyhow::Result;
use symbolic::{
    debuginfo::{pe, Object},
    symcache::{SymCache, SymCacheWriter},
};

/// Caching provider of debug info for executable code modules.
#[derive(Default)]
pub struct DebugInfo {
    // Cached debug info, keyed by module path.
    modules: HashMap<PathBuf, ModuleDebugInfo>,

    // Set of module paths known to lack debug info.
    no_debug_info: HashSet<PathBuf>,
}

impl DebugInfo {
    /// Try to load debug info for a module.
    ///
    /// If debug info was founded and loaded (now or previously), returns
    /// `true`. If the module does not have debug info, returns `false`.
    pub fn load_module(&mut self, module: PathBuf) -> Result<bool> {
        if self.no_debug_info.contains(&module) {
            return Ok(false);
        }

        if self.modules.get(&module).is_some() {
            return Ok(true);
        }

        let info = match ModuleDebugInfo::load(&module)? {
            Some(info) => info,
            None => {
                self.no_debug_info.insert(module);
                return Ok(false);
            }
        };

        self.modules.insert(module, info);

        Ok(true)
    }

    /// Fetch debug info for `module`, if loaded.
    ///
    /// Does not attempt to load debug info for the module.
    pub fn get(&self, module: impl AsRef<Path>) -> Option<&ModuleDebugInfo> {
        self.modules.get(module.as_ref())
    }
}

/// Debug info for a single executable module.
pub struct ModuleDebugInfo {
    /// Backing debug info file data for the module.
    ///
    /// May not include the actual executable code.
    pub object: Object<'static>,

    /// Cache which allows efficient source line lookups.
    pub source: SymCache<'static>,
}

impl ModuleDebugInfo {
    /// Load debug info for a module.
    ///
    /// Returns `None` when the module was found and loadable, but no matching
    /// debug info could be found.
    ///
    /// Leaks module and symbol data.
    fn load(module: &Path) -> Result<Option<Self>> {
        let mut data = fs::read(&module)?.into_boxed_slice();

        // If our module is a PE file, the debug info will be in the PDB.
        //
        // We will need a similar check to support split DWARF.
        let is_pe = pe::PeObject::test(&data);
        if is_pe {
            // Assume a sibling PDB.
            //
            // TODO: Find PDB using `dbghelp::`SymGetSymbolFile()`.
            let pdb = module.with_extension("pdb");
            data = fs::read(&pdb)?.into_boxed_slice();
        }

        // Now we're sure we want this data, so leak it.
        let data = Box::leak(data);

        let object = Object::parse(data)?;

        if !object.has_debug_info() {
            return Ok(None);
        }

        let cursor = io::Cursor::new(vec![]);
        let cursor = SymCacheWriter::write_object(&object, cursor)?;
        let cache_data = Box::leak(cursor.into_inner().into_boxed_slice());
        let source = SymCache::parse(cache_data)?;

        Ok(Some(Self { object, source }))
    }
}
