// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{HashMap, HashSet};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use anyhow::Result;
use symbolic::{
    debuginfo::Object,
    symcache::{SymCache, SymCacheWriter},
};

#[cfg(windows)]
use goblin::pe::PE;

#[cfg(windows)]
use symbolic::debuginfo::pe;

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
        // Used when `cfg(windows)`.
        #[allow(unused_mut)]
        let mut data = fs::read(&module)?.into_boxed_slice();

        // Conditional so we can use `dbghelp`.
        #[cfg(windows)]
        {
            // If our module is a PE file, the debug info will be in the PDB.
            //
            // We will need a similar check to support split DWARF.
            let is_pe = pe::PeObject::test(&data);
            if is_pe {
                let pe = PE::parse(&data)?;

                // Search the symbol path for a PDB for this PE, which we'll use instead.
                if let Some(pdb) = crate::pdb::find_pdb_path(module, &pe, None)? {
                    data = fs::read(&pdb)?.into_boxed_slice();
                }
            }
        }

        // Now we're more sure we want this data. Leak it so the parsed object
        // will have a `static` lifetime.
        let data = Box::leak(data);

        // Save a raw pointer to the file data. If object parsing fails, or
        // there is no debuginfo, we will use this to avoid leaking memory.
        let data_ptr = data as *mut _;

        let object = match Object::parse(data) {
            Ok(object) => {
                if !object.has_debug_info() {
                    // Drop the object to drop its static references to the leaked data.
                    drop(object);

                    // Reconstruct to free data on drop.
                    //
                    // Safety: we leaked this box locally, and only `object` had a reference to it
                    // via `Object::parse()`. We manually dropped `object`, so the raw pointer is no
                    // longer aliased.
                    unsafe {
                        Box::from_raw(data_ptr);
                    }

                    return Ok(None);
                }

                object
            }
            Err(err) => {
                // Reconstruct to free data on drop.
                //
                // Safety: we leaked this box locally, and only passed the leaked ref once, to
                // `Object::parse()`. In this branch, it returned an `ObjectError`, which does not
                // hold a reference to the leaked data. The raw pointer is no longer aliased, so we
                // can both free its referent and also return the error.
                unsafe {
                    Box::from_raw(data_ptr);
                }

                return Err(err.into());
            }
        };

        let cursor = io::Cursor::new(vec![]);
        let cursor = SymCacheWriter::write_object(&object, cursor)?;
        let cache_data = Box::leak(cursor.into_inner().into_boxed_slice());

        // Save a raw pointer to the cache data. If cache parsing fails, we will use this to
        // avoid leaking memory.
        let cache_data_ptr = cache_data as *mut _;

        match SymCache::parse(cache_data) {
            Ok(source) => Ok(Some(Self { object, source })),
            Err(err) => {
                // Reconstruct to free data on drop.
                //
                // Safety: we leaked this box locally, and only passed the leaked ref once, to
                // `SymCache::parse()`. In this branch, it returned a `SymCacheError`, which does
                // not hold a reference to the leaked data. The pointer is no longer aliased, so we
                // can both free its referent and also return the error.
                unsafe {
                    Box::from_raw(cache_data_ptr);
                }

                Err(err.into())
            }
        }
    }
}
