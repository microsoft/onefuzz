// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::fs::File;
use std::path::{Path, PathBuf};

use anyhow::{format_err, Result};
use log::*;
use pdb::{FallibleIterator, SymbolData, PDB};
use serde::{Deserialize, Serialize};

use crate::SrcLine;

#[derive(Clone, Debug, Eq, Hash, PartialEq, Serialize, Deserialize)]
pub struct PdbCache {
    offset_to_line: BTreeMap<usize, SrcLine>,
    symbol_to_lines: BTreeMap<String, Vec<SrcLine>>,
    path_to_symbols: BTreeMap<PathBuf, Vec<String>>,
    path_to_lines: BTreeMap<PathBuf, Vec<usize>>,
}

impl PdbCache {
    pub fn new<P: AsRef<Path>>(pdb: P) -> Result<Self> {
        let mut offset_to_line: BTreeMap<usize, SrcLine> = BTreeMap::new();
        let mut symbol_to_lines: BTreeMap<String, Vec<SrcLine>> = BTreeMap::new();

        // NOTE: We're using strings as the keys for now while we build the trees, since
        // PathBuf comparisons are expensive.
        let mut path_to_symbols: BTreeMap<String, Vec<String>> = BTreeMap::new();
        let mut path_to_lines: BTreeMap<String, Vec<usize>> = BTreeMap::new();

        let pdbfile = File::open(pdb)?;
        let mut pdb = PDB::open(pdbfile)?;

        let address_map = pdb.address_map()?;
        let string_table = pdb.string_table()?;

        let dbi = pdb.debug_information()?;
        let mut modules = dbi.modules()?;
        while let Some(module) = modules.next()? {
            let info = match pdb.module_info(&module)? {
                Some(info) => info,
                None => {
                    warn!("no module info: {:?}", &module);
                    continue;
                }
            };

            let program = info.line_program()?;
            let mut symbols = info.symbols()?;

            while let Some(symbol) = symbols.next()? {
                if let Ok(SymbolData::Procedure(proc)) = symbol.parse() {
                    let proc_name = proc.name.to_string();
                    let mut lines = program.lines_for_symbol(proc.offset);

                    let symbol_to_lines = symbol_to_lines.entry(proc_name.to_string()).or_default();

                    while let Some(line_info) = lines.next()? {
                        let rva = line_info
                            .offset
                            .to_rva(&address_map)
                            .ok_or_else(|| format_err!("invalid RVA: {:?}", line_info))?;
                        let file_info = program.get_file_info(line_info.file_index)?;
                        let file_name = file_info.name.to_string_lossy(&string_table)?;

                        let path = file_name.into_owned();
                        let line = line_info.line_start as usize;

                        let srcloc = SrcLine::new(path.clone(), line);

                        offset_to_line.insert(rva.0 as usize, srcloc.clone());
                        path_to_symbols
                            .entry(path.clone())
                            .or_default()
                            .push(proc_name.to_string());
                        symbol_to_lines.push(srcloc.clone());
                        path_to_lines.entry(path.clone()).or_default().push(line);
                    }
                }
            }
        }

        Ok(Self {
            offset_to_line,
            symbol_to_lines,
            path_to_symbols: path_to_symbols
                .into_iter()
                .map(|(p, s)| (PathBuf::from(p), s))
                .collect(),
            path_to_lines: path_to_lines
                .into_iter()
                .map(|(p, l)| (PathBuf::from(p), l))
                .collect(),
        })
    }

    pub fn offset(&self, off: &usize) -> Option<&SrcLine> {
        self.offset_to_line.get(off)
    }

    pub fn paths(&self) -> impl Iterator<Item = &PathBuf> {
        self.path_to_lines.keys()
    }

    pub fn path_symbols<P: AsRef<Path>>(&self, path: P) -> Option<impl Iterator<Item = &str>> {
        self.path_to_symbols
            .get(path.as_ref())
            .map(|x| x.iter().map(|y| y.as_str()))
    }

    pub fn path_lines<P: AsRef<Path>>(&self, path: P) -> Option<impl Iterator<Item = &usize>> {
        self.path_to_lines.get(path.as_ref()).map(|x| x.iter())
    }

    pub fn symbol(&self, sym: &str) -> Option<impl Iterator<Item = &SrcLine>> {
        self.symbol_to_lines.get(sym).map(|x| x.iter())
    }
}
