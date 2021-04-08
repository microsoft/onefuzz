// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::borrow::Borrow;
use std::ffi::OsStr;
use std::fmt;
use std::ops::Range;
use std::path::{Path, PathBuf};

use anyhow::{bail, Result};
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
        let path = dunce::canonicalize(path)?;
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

/// Index over an executable module and its symbols.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ModuleIndex {
    /// Absolute path to the module's backing file.
    pub path: ModulePath,

    /// Preferred virtual address of the module's base image.
    pub base_va: u64,

    /// Index over the module's symbols.
    pub symbols: SymbolIndex,
}

impl ModuleIndex {
    /// Build a new index over a parsed ELF module.
    #[cfg(target_os = "linux")]
    pub fn index_elf(path: ModulePath, elf: &goblin::elf::Elf) -> Result<Self> {
        use anyhow::format_err;
        use goblin::elf::program_header::PT_LOAD;

        // Calculate the module base address as the lowest preferred VA of any loadable segment.
        //
        // https://refspecs.linuxbase.org/elf/gabi4+/ch5.pheader.html#base_address
        let base_va = elf
            .program_headers
            .iter()
            .filter(|h| h.p_type == PT_LOAD)
            .map(|h| h.p_vaddr)
            .min()
            .ok_or_else(|| format_err!("no loadable segments for ELF object ({})", path))?;

        let mut symbols = SymbolIndex::default();

        for sym in elf.syms.iter() {
            if sym.st_size == 0 {
                log::debug!("skipping size 0 symbol: {:x?}", sym);
                continue;
            }

            if sym.is_function() {
                let name = match elf.strtab.get(sym.st_name) {
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
                let section = elf
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

                let symbol = Symbol::new(name, sym_file_offset, image_offset, sym.st_size);

                match symbol {
                    Ok(entry) => {
                        let inserted = symbols.index.insert(entry.clone());
                        if !inserted {
                            log::error!("failed to insert symbol index entry: {:x?}", entry);
                        }
                    }
                    Err(err) => {
                        log::error!("invalid symbol: err = {}", err);
                    }
                }
            }
        }

        Ok(Self {
            path,
            base_va,
            symbols,
        })
    }

    /// Build a new index over a parsed PE module.
    #[cfg(target_os = "windows")]
    pub fn index_pe(path: ModulePath, pe: &goblin::pe::PE) -> Self {
        let base_va = pe.image_base as u64;

        // Not yet implemented.
        let symbols = SymbolIndex::default();

        Self {
            path,
            base_va,
            symbols,
        }
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
    pub fn new(name: String, file_offset: u64, image_offset: u64, size: u64) -> Result<Self> {
        if name.is_empty() {
            bail!("symbol name cannot be empty");
        }

        if size == 0 {
            bail!("symbol size must be nonzero");
        }

        if file_offset.checked_add(size).is_none() {
            bail!("symbol size must not overflow file offset");
        }

        if image_offset.checked_add(size).is_none() {
            bail!("symbol size must not overflow image offset");
        }

        Ok(Self {
            name,
            file_offset,
            image_offset,
            size,
        })
    }

    pub fn file_range(&self) -> Range<u64> {
        let lo = self.file_offset;

        // Overflow checked in constructor.
        let hi = lo + self.size;

        lo..hi
    }

    pub fn file_range_usize(&self) -> Range<usize> {
        let lo = self.file_offset as usize;

        // Overflow checked in constructor.
        let hi = lo + (self.size as usize);

        lo..hi
    }

    pub fn image_range(&self) -> Range<u64> {
        let lo = self.image_offset;
        let hi = lo + self.size;
        lo..hi
    }

    pub fn contains_file_offset(&self, offset: u64) -> bool {
        self.file_range().contains(&offset)
    }

    pub fn contains_image_offset(&self, offset: u64) -> bool {
        self.image_range().contains(&offset)
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
pub struct CmdFilterDef {
    defs: Vec<ModuleRuleDef>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct ModuleRuleDef {
    pub module: String,

    #[serde(flatten)]
    pub rule: RuleDef,
}

/// User-facing encoding of a module-tracking rule.
///
/// We use an intermediate type to expose a rich and easily-updated user-facing
/// format for expressing rules, while decoupling the `serde` machinery from the
/// normalized type used for business logic.
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(untagged)]
enum RuleDef {
    Include {
        include: bool,
    },
    Exclude {
        exclude: bool,
    },

    // Temporarily disable symbol filtering rules.
    #[cfg_attr(not(feature = "symbol-filter"), serde(skip), allow(unused))]
    Filter(Box<Filter>),
}

/// A normalized encoding of a module-tracking rule.
#[derive(Clone, Debug)]
enum Rule {
    /// Asserts that the entire module should be be tracked (and its symbols
    /// included), or ignored, and its symbols excluded.
    ///
    /// The implied symbol tracking behavior could be encoded by a filter, but a
    /// distinction at this level lets us avoid parsing modules that we want to
    /// ignore.
    IncludeModule(bool),

    /// The entire module should be tracked and parsed, with a filter applied to
    /// its symbols.
    FilterSymbols(Box<Filter>),
}

impl From<RuleDef> for Rule {
    fn from(def: RuleDef) -> Self {
        match def {
            RuleDef::Exclude { exclude } => Rule::IncludeModule(!exclude),
            RuleDef::Include { include } => Rule::IncludeModule(include),
            RuleDef::Filter(filter) => Rule::FilterSymbols(filter),
        }
    }
}

/// Module and symbol-tracking rules to be applied to a command.
#[derive(Clone, Debug)]
pub struct CmdFilter {
    regexes: RegexSet,
    rules: Vec<Rule>,
}

impl CmdFilter {
    pub fn new(cmd: CmdFilterDef) -> Result<Self> {
        let mut modules = vec![];
        let mut rules = vec![];

        for def in cmd.defs {
            modules.push(def.module);
            rules.push(def.rule.into());
        }

        let regexes = RegexSet::new(&modules)?;

        Ok(Self { regexes, rules })
    }

    pub fn includes_module(&self, module: &ModulePath) -> bool {
        match self.regexes.matches(&module.path_lossy()).iter().next() {
            Some(index) => {
                // In-bounds by construction.
                match &self.rules[index] {
                    Rule::IncludeModule(included) => *included,
                    Rule::FilterSymbols(_) => {
                        // A filtered module is implicitly tracked.
                        true
                    }
                }
            }
            None => {
                // Track modules by default.
                true
            }
        }
    }

    pub fn includes_symbol(&self, module: &ModulePath, symbol: impl AsRef<str>) -> bool {
        match self.regexes.matches(&module.path_lossy()).iter().next() {
            Some(index) => {
                // In-bounds by construction.
                match &self.rules[index] {
                    Rule::IncludeModule(included) => *included,
                    Rule::FilterSymbols(filter) => filter.includes(symbol.as_ref()),
                }
            }
            None => {
                // Include symbols by default.
                true
            }
        }
    }
}

impl Default for CmdFilter {
    fn default() -> Self {
        let def = CmdFilterDef::default();

        // An empty set of filter definitions has no regexes, which means when
        // constructing, we never internally risk compiling an invalid regex.
        Self::new(def).expect("unreachable")
    }
}

#[cfg(test)]
mod tests;
