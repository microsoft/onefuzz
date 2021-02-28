// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::borrow::Borrow;
use std::collections::{BTreeMap, HashMap};
use std::ffi::OsStr;
use std::fmt;
use std::path::{Path, PathBuf};

use anyhow::{bail, format_err, Result};
use goblin::elf;
use regex::RegexSet;
use serde::{Deserialize, Serialize};

use crate::filter::Filter;
use crate::region::{Region, RegionIndex};

/// `PathBuf` that is guaranteed to be canonicalized and have a file name.
#[derive(Clone, Debug, Deserialize, Eq, Hash, Ord, PartialEq, PartialOrd, Serialize)]
#[serde(transparent)]
pub struct ModulePath {
    path: PathBuf,
}

impl fmt::Display for ModulePath {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self.path.display())
    }
}

impl ModulePath {
    /// Validate that `path` is absolute and has a filename.
    pub fn new(path: PathBuf) -> Result<Self> {
        if path.is_relative() {
            bail!("module path is not absolute");
        }

        if path.file_name().is_none() {
            bail!("module path has no file name");
        }

        Ok(Self { path })
    }

    pub fn existing(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref().canonicalize()?;
        Self::new(path)
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn path_lossy(&self) -> String {
        self.path.to_string_lossy().into_owned()
    }

    pub fn name(&self) -> &OsStr {
        // Unwrap checked in constructor.
        self.path.file_name().unwrap()
    }

    pub fn name_lossy(&self) -> String {
        self.name().to_string_lossy().into_owned()
    }
}

impl AsRef<Path> for ModulePath {
    fn as_ref(&self) -> &Path {
        self.path()
    }
}

impl AsRef<OsStr> for ModulePath {
    fn as_ref(&self) -> &OsStr {
        self.path().as_ref()
    }
}

impl Borrow<Path> for ModulePath {
    fn borrow(&self) -> &Path {
        self.path()
    }
}

impl From<ModulePath> for PathBuf {
    fn from(module_path: ModulePath) -> PathBuf {
        module_path.path
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ModuleIndex {
    pub path: ModulePath,
    pub base_va: u64,
    pub symbols: SymbolIndex,
}

impl ModuleIndex {
    #[cfg(target_os = "linux")]
    pub fn parse_elf(path: ModulePath, data: &[u8]) -> Result<Self> {
        use elf::program_header::PT_LOAD;

        let object = elf::Elf::parse(data)?;

        // Calculate the module base address as the lowest preferred VA of any loadable segment.
        //
        // https://refspecs.linuxbase.org/elf/gabi4+/ch5.pheader.html#base_address
        let base_va = object
            .program_headers
            .iter()
            .filter(|h| h.p_type == PT_LOAD)
            .map(|h| h.p_vaddr)
            .min()
            .ok_or_else(|| format_err!("no loadable segments for ELF object ({})", path))?;

        let mut symbols = SymbolIndex::default();

        for sym in object.syms.iter() {
            if sym.st_size == 0 {
                log::debug!("skipping size 0 symbol: {:x?}", sym);
                continue;
            }

            if sym.is_function() {
                let name = match object.strtab.get(sym.st_name) {
                    None => {
                        log::error!("symbol not found in symbol string table: {:?}", sym);
                        continue;
                    }
                    Some(Err(err)) => {
                        log::error!(
                            "unable to parse symbol name: sym = {:?}, err = {}",
                            sym,
                            err
                        );
                        continue;
                    }
                    Some(Ok(name)) => name.to_owned(),
                };

                // For executables and shared objects, `st_value` contains the VA of the symbol.
                //
                // https://refspecs.linuxbase.org/elf/gabi4+/ch4.symtab.html#symbol_value
                let sym_va = sym.st_value;

                // The module-relative offset of the mapped symbol is immediate.
                let image_offset = sym_va - base_va;

                // We want to make it easy to read the symbol from the file on disk. To do this, we
                // need to compute its file offset.
                //
                // A symbol is defined relative to some section, identified by `st_shndx`, an index
                // into the section header table. We'll use the section header to compute the file
                // offset of the symbol.
                let section = object
                    .section_headers
                    .get(sym.st_shndx)
                    .cloned()
                    .ok_or_else(|| format_err!("invalid section table index for symbol"))?;

                // If mapped into a segment, `sh_addr` contains the VA of the section image,
                // consistent with the `p_vaddr` of the segment.
                //
                // https://refspecs.linuxbase.org/elf/gabi4+/ch4.sheader.html#section_header
                let section_va = section.sh_addr;

                // The offset of the symbol relative to its section (both in-file and when mapped).
                let sym_section_offset = sym_va - section_va;

                // We have the file offset for the section via `sh_offset`, and the offset of the
                // symbol within the section. From this, calculate the file offset for the symbol,
                // which we can use to index into `data`.
                let sym_file_offset = section.sh_offset + sym_section_offset;

                let entry = Symbol {
                    name,
                    file_offset: sym_file_offset,
                    image_offset,
                    size: sym.st_size,
                };

                let inserted = symbols.index.insert(entry.clone());
                if !inserted {
                    log::error!("failed to insert symbol index entry: {:x?}", entry);
                }
            }
        }

        Ok(Self {
            path,
            base_va,
            symbols,
        })
    }
}

#[derive(Clone, Debug, Default, Deserialize, Eq, PartialEq, Serialize)]
#[serde(transparent)]
pub struct SymbolIndex {
    pub index: RegionIndex<Symbol>,
}

impl SymbolIndex {
    pub fn iter(&self) -> impl Iterator<Item = &Symbol> {
        self.index.iter()
    }

    /// Find the symbol metadata for the image-relative `offset`.
    pub fn find(&self, offset: u64) -> Option<&Symbol> {
        self.index.find(offset)
    }
}

#[derive(Clone, Debug, Default, Deserialize, Eq, PartialEq, Serialize)]
pub struct Symbol {
    /// Raw symbol name, possibly mangled.
    pub name: String,

    /// File offset of the symbol definition in the on-disk module.
    pub file_offset: u64,

    /// Module-relative offset of the mapped symbol.
    pub image_offset: u64,

    /// Total size in bytes of the symbol definition.
    pub size: u64,
}

impl Symbol {
    pub fn contains_file_offset(&self, offset: u64) -> bool {
        let lo = self.file_offset;
        let hi = lo + self.size;
        (lo..hi).contains(&offset)
    }

    pub fn contains_image_offset(&self, offset: u64) -> bool {
        let lo = self.image_offset;
        let hi = lo + self.size;
        (lo..hi).contains(&offset)
    }
}

/// Symbol metadata defines a `Region` relative to its process image.
impl Region for Symbol {
    fn base(&self) -> u64 {
        self.image_offset
    }

    fn size(&self) -> u64 {
        self.size
    }
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(transparent)]
pub struct SymbolFilterSpec {
    filters: HashMap<String, Filter>,
}

#[derive(Clone, Debug)]
pub struct SymbolFilter {
    /// Pre-compiled regex set for fast matching.
    regexes: RegexSet,

    /// Maps module name regexes to filters for symbol names.
    filters: BTreeMap<usize, Filter>,
}

impl SymbolFilter {
    pub fn new(mut spec: SymbolFilterSpec) -> Result<Self> {
        let regexes = RegexSet::new(spec.filters.keys())?;

        let mut filters = BTreeMap::default();

        for (idx, pat) in regexes.patterns().iter().enumerate() {
            // Guaranteed by construction.
            let filter = spec.filters.remove(pat).unwrap();

            filters.insert(idx, filter);
        }

        Ok(Self { regexes, filters })
    }

    pub fn is_allowed(&self, module: &ModulePath, name: &str) -> bool {
        let rules = self.regexes.matches(&module.path_lossy());

        // Check if there is some symbol rule for the module path.
        //
        // If many rules would apply, the first rule is used.
        if let Some(idx) = rules.iter().next() {
            // Guaranteed by constructor.
            let filter = self.filters.get(&idx).unwrap();

            filter.is_allowed(name)
        } else {
            // If no module-level rule exists, allow by default.
            true
        }
    }
}

impl Default for SymbolFilter {
    fn default() -> Self {
        let spec = SymbolFilterSpec::default();

        // Cannot fail when spec is empty.
        SymbolFilter::new(spec).unwrap()
    }
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
pub struct CmdFilterSpec {
    pub modules: Filter,
    pub symbols: SymbolFilterSpec,
}

#[derive(Clone, Debug, Default)]
pub struct CmdFilter {
    pub modules: Filter,
    pub symbols: SymbolFilter,
}

impl CmdFilter {
    pub fn new(spec: CmdFilterSpec) -> Result<Self> {
        let modules = spec.modules;
        let symbols = SymbolFilter::new(spec.symbols)?;

        Ok(Self { modules, symbols })
    }
}
