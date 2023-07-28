// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};

use anyhow::Result;
use serde::{Deserialize, Serialize};

use crate::{ModOff, PdbCache, SrcLine};

#[derive(Clone, Debug, Default, Eq, PartialEq, Serialize, Deserialize)]
pub struct SrcView(BTreeMap<String, PdbCache>);

/// A SrcView is a collection of zero or more PdbCaches for easy querying. It stores all
/// the mapping information from the PDBs. It does _not_ contain any coverage information.
impl SrcView {
    /// Create's a new SrcView
    pub fn new() -> Self {
        Self::default()
    }

    /// Insert a new pdb into the SrcView, returning any previous pdb info that you're
    /// replacing. In most cases this return value can be ignored.
    ///
    /// # Arguments
    ///
    /// * `module` - Module name to store the PDB info as
    /// * `pdb` - Path to PDB
    ///
    /// # Errors
    ///
    ///  If the PDB at the provided path cannot be parsed or contains otherwise unexpected data.
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// // you can now query sv for info from example.exe...
    /// ```
    pub fn insert<P: AsRef<Path>>(&mut self, module: &str, pdb: P) -> Result<Option<PdbCache>> {
        let cache = PdbCache::new(pdb)?;
        Ok(self.0.insert(module.to_owned(), cache))
    }

    /// Insert a new pdb into the SrcView only if the `pdb` path is not in the SrcView already,
    /// returning a  [Result] indicating the success of the insert, if any was necessary.
    /// If the [Result] is [Ok], the contained bool indicates whether a value was inserted.
    ///
    /// # Arguments
    ///
    /// * `module` - Module name to store the PDB info as
    /// * `pdb` - Path to PDB
    ///
    /// # Errors
    ///
    /// If the PDB at the provided path has not been inserted already **and**
    /// cannot be parsed or contains otherwise unexpected data.
    ///
    /// # Example
    ///
    /// ```ignore
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    /// let modoffs = get_coverage();
    ///
    /// // Map each modoff to a PDB name/path and make sure it's in the SrcView
    /// for modoff in modoffs {
    ///     let module_name =  mod_name_from_modoff(modoff);
    ///     let res = sv.try_insert(module_name, format!("~/pdbs/{module_name}.pdb"));
    ///
    ///     if let Ok(inserted) = res {
    ///         println!("PDB was inserted: {inserted}");
    ///     }
    /// }
    /// ```
    pub fn try_insert<P: AsRef<Path>>(&mut self, module: &str, pdb: P) -> Result<bool> {
        if self.0.contains_key(&module.to_owned()) {
            Ok(false)
        } else {
            self.insert(module, pdb).map(|_| true)
        }
    }

    /// Resolve a modoff to a number of SrcLine(s), if any exists. Note that
    /// there may be multiple SrcLines mapped to this modoff if that modoff
    /// represents some inlined function
    ///
    /// # Arguments
    ///
    /// * `modoff` - Reference to a ModOff you'd like to resolve
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::{ModOff, SrcView};
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// let modoff = ModOff::new("example.exe", 0x4141);
    ///
    /// if let Some(srclines) = sv.modoff(&modoff) {
    ///     println!("example.exe+{modoff} has the following valid source lines:");
    ///     for line in srclines {
    ///         println!(" - {line}", line);
    ///     }
    /// };
    /// ```
    pub fn modoff(&self, modoff: &ModOff) -> Option<impl Iterator<Item = &SrcLine>> {
        match self.0.get(&modoff.module) {
            Some(cache) => cache.offset(&modoff.offset),
            None => None,
        }
    }

    /// Resolve a symbol (e.g. module!name) to its possible SrcLines, if such a symbol
    /// exists
    ///
    /// # Arguments
    ///
    /// * `sym` - A symbol name you'd like to query in the form of `<module>!<name>`
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// if let Some(srclines) = sv.symbol("example.exe!main") {
    ///     println!("example.exe!main has the following valid source lines:");
    ///     for line in srclines {
    ///         println!(" - {}", line);
    ///     }
    /// };
    /// ```
    pub fn symbol(&self, sym: &str) -> Option<impl Iterator<Item = &SrcLine>> {
        let split: Vec<&str> = sym.split('!').collect();

        if split.len() < 2 {
            return None;
        }

        let module = split[0];
        let name: String = split[1..].join("!");

        match self.0.get(module) {
            Some(cache) => cache.symbol(&name),
            None => None,
        }
    }

    /// Resolve a path to its possible SrcLines, if such a path exists
    ///
    /// # Arguments
    ///
    /// * `path` - An absolute path that possibly matches one from the debug info
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// if let Some(srclines) = sv.path_lines(r"z:\src\example.c") {
    ///     println!("example.c has the following valid source lines:");
    ///     for line in srclines {
    ///         println!(" - {}", line);
    ///     }
    /// }
    /// ```
    pub fn path_lines<P: AsRef<Path>>(&self, path: P) -> Option<impl Iterator<Item = usize>> {
        // we want to unique the lines in use across all loaded pdbs
        let mut r: BTreeSet<usize> = BTreeSet::new();

        for cache in self.0.values() {
            if let Some(lines) = cache.path_lines(path.as_ref()) {
                for line in lines {
                    r.insert(*line);
                }
            }
        }

        if r.is_empty() {
            return None;
        }

        // convert to a vec
        let mut v: Vec<usize> = r.into_iter().collect();

        // lists of line numbers are nicer when theyre sorted...
        v.sort_unstable();

        Some(v.into_iter())
    }

    /// Resolve a path to its possible symbols, if such a path exists
    ///
    /// # Arguments
    ///
    /// * `path` - An absolute path that possibly matches one from the debug info
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// if let Some(symbols) = sv.path_symbols(r"z:\src\example.c") {
    ///     println!("example.c has the following valid symbols:");
    ///     for line in symbols {
    ///         println!(" - {}", line);
    ///     }
    /// }
    /// ```
    pub fn path_symbols<P: AsRef<Path>>(&self, path: P) -> Option<impl Iterator<Item = String>> {
        // we want to unique the lines in use across all loaded pdbs
        let mut r: BTreeSet<String> = BTreeSet::new();

        for (module, cache) in self.0.iter() {
            if let Some(symbols) = cache.path_symbols(path.as_ref()) {
                for sym in symbols {
                    r.insert(format!("{module}!{sym}"));
                }
            }
        }

        if r.is_empty() {
            return None;
        }

        // convert to a vec
        let mut v: Vec<String> = r.into_iter().collect();

        // lists of symbols are nicer when theyre sorted...
        v.sort();

        Some(v.into_iter())
    }

    /// Returns an iterator over all paths in the SrcView
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::SrcView;
    ///
    /// let mut sv = SrcView::new();
    ///
    /// // Map the contents of 'example.pdb' to the module name 'example.exe'
    /// sv.insert("example.exe", r"z:\src\example.pdb").unwrap();
    ///
    /// println!("paths in example.pdb:");
    ///
    /// for path in sv.paths() {
    ///     println!(" - {}", path.display());
    /// }
    /// ```
    pub fn paths(&self) -> impl Iterator<Item = &PathBuf> {
        let mut r: BTreeSet<&PathBuf> = BTreeSet::new();

        for cache in self.0.values() {
            for pb in cache.paths() {
                r.insert(pb);
            }
        }

        r.into_iter()
    }
}
