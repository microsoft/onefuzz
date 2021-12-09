use std::collections::{HashMap, HashSet};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use anyhow::Result;
use symbolic::{
    debuginfo::{pe, Object, ObjectDebugSession},
    symcache::{SymCache, SymCacheWriter},
};

#[derive(Default)]
pub struct DebugInfo {
    modules: HashMap<PathBuf, ModuleDebugInfo>,
    no_debug_info: HashSet<PathBuf>,
}

impl DebugInfo {
    pub fn load_module(&mut self, module: PathBuf) -> Result<bool> {
        if self.no_debug_info.contains(&module) {
            return Ok(false);
        }

        if self.modules.get(&module).is_some() {
            return Ok(true);
        }

        let info = ModuleDebugInfo::load(&module);

        if info.is_err() {
            self.no_debug_info.insert(module);
            return Ok(false);
        }
        let info = info?;

        self.modules.insert(module, info);

        Ok(true)
    }

    pub fn get(&self, module: impl AsRef<Path>) -> Option<&ModuleDebugInfo> {
        self.modules.get(module.as_ref())
    }
}

pub struct ModuleDebugInfo {
    pub object: Object<'static>,
    pub session: ObjectDebugSession<'static>,
    pub source: SymCache<'static>,
}

impl ModuleDebugInfo {
    /// Load debug info for a module.
    ///
    /// Leaks module and symbol data.
    fn load(module: &Path) -> Result<Self> {
        let mut data = fs::read(&module)?.into_boxed_slice();

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
            anyhow::bail!("no debug info!");
        }

        let session = object.debug_session()?;

        let cursor = SymCacheWriter::write_object(&object, io::Cursor::new(vec![]))?;
        let data = Box::leak(cursor.into_inner().into_boxed_slice());
        let source = SymCache::parse(data)?;

        Ok(Self {
            object,
            session,
            source,
        })
    }
}
