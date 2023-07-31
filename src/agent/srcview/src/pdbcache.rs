// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::fs::File;
use std::path::{Path, PathBuf};

use anyhow::{bail, format_err, Result};
use log::*;
use pdb::{FallibleIterator, SymbolData, PDB};
use serde::{Deserialize, Serialize};

use crate::SrcLine;

// NOTE: We're using strings as the keys for now while we build the trees, since
// PathBuf comparisons are expensive.
#[derive(Default)]
struct PdbCacheBuilder {
    offset_to_line: BTreeMap<usize, Vec<SrcLine>>,
    symbol_to_lines: BTreeMap<String, Vec<SrcLine>>,
    path_to_symbols: BTreeMap<String, Vec<String>>,
    path_to_lines: BTreeMap<String, Vec<usize>>,
}

impl PdbCacheBuilder {
    fn build(self) -> PdbCache {
        PdbCache {
            offset_to_line: self.offset_to_line,
            symbol_to_lines: self.symbol_to_lines,
            path_to_symbols: self
                .path_to_symbols
                .into_iter()
                .map(|(p, s)| (PathBuf::from(p), s))
                .collect(),
            path_to_lines: self
                .path_to_lines
                .into_iter()
                .map(|(p, l)| (PathBuf::from(p), l))
                .collect(),
        }
    }

    fn update_from_iter<I: FallibleIterator<Item = pdb::LineInfo, Error = pdb::Error>>(
        &mut self,
        program: &pdb::LineProgram,
        string_table: &pdb::StringTable,
        address_map: &pdb::AddressMap,
        proc_name: &str,
        mut lines: I,
    ) -> Result<()> {
        let symbol_to_lines = self
            .symbol_to_lines
            .entry(proc_name.to_string())
            .or_default();

        while let Some(line_info) = lines.next()? {
            let rva = line_info
                .offset
                .to_rva(address_map)
                .ok_or_else(|| format_err!("invalid RVA: {:?}", line_info))?;
            let file_info = program.get_file_info(line_info.file_index)?;
            let file_name = file_info.name.to_string_lossy(string_table)?;

            let path = file_name.into_owned();
            let offset_to_line = self.offset_to_line.entry(rva.0 as usize).or_default();
            let path_to_symbols = self.path_to_symbols.entry(path.clone()).or_default();
            let path_to_lines = self.path_to_lines.entry(path.clone()).or_default();

            for line in line_info.line_start as usize..line_info.line_end as usize + 1 {
                let srcloc = SrcLine::new(path.clone(), line);

                offset_to_line.push(srcloc.clone());
                symbol_to_lines.push(srcloc.clone());
                path_to_symbols.push(proc_name.to_string());
                path_to_lines.push(line);
            }
        }

        Ok(())
    }
}

#[derive(Clone, Debug, Eq, Hash, PartialEq, Serialize, Deserialize)]
pub struct PdbCache {
    offset_to_line: BTreeMap<usize, Vec<SrcLine>>,
    symbol_to_lines: BTreeMap<String, Vec<SrcLine>>,
    path_to_symbols: BTreeMap<PathBuf, Vec<String>>,
    path_to_lines: BTreeMap<PathBuf, Vec<usize>>,
}

impl PdbCache {
    pub fn new<P: AsRef<Path>>(pdb: P) -> Result<Self> {
        let mut builder = PdbCacheBuilder::default();

        let pdbfile = File::open(pdb)?;
        let mut pdb = PDB::open(pdbfile)?;

        let address_map = pdb.address_map()?;
        let string_table = pdb.string_table()?;

        let dbi = pdb.debug_information()?;
        let mut modules = dbi.modules()?;

        let ids = pdb.id_information()?;
        let mut id_finder = ids.finder();
        let mut id_iter = ids.iter();
        while id_iter.next()?.is_some() {
            id_finder.update(&id_iter);
        }

        while let Some(module) = modules.next()? {
            let info = match pdb.module_info(&module)? {
                Some(info) => info,
                None => {
                    warn!("no module info: {:?}", &module);
                    continue;
                }
            };

            // Gather all inlinee entries
            let mut inlinees = BTreeMap::new();
            let mut inlinee_iter = info.inlinees()?;
            while let Some(inlinee) = inlinee_iter.next()? {
                let item = id_finder.find(inlinee.index())?;
                let fname = match item.parse()? {
                    pdb::IdData::Function(fid) => fid.name,
                    pdb::IdData::MemberFunction(mid) => mid.name,
                    _ => {
                        bail!(
                            "Expected LF_MFUC_ID or LF_FUNC_ID for {} but got type {}",
                            inlinee.index(),
                            item.raw_kind(),
                        );
                    }
                };
                inlinees.insert(inlinee.index(), (inlinee, fname));
            }

            let program = info.line_program()?;
            let mut symbols = info.symbols()?;

            while let Some(symbol) = symbols.next()? {
                match symbol.parse() {
                    Ok(SymbolData::Procedure(proc)) => {
                        let lines = program.lines_for_symbol(proc.offset);
                        let proc_name = proc.name.to_string();
                        builder.update_from_iter(
                            &program,
                            &string_table,
                            &address_map,
                            &proc_name,
                            lines,
                        )?;
                    }
                    Ok(SymbolData::InlineSite(site)) => {
                        // Locate the parent proc of this inline site
                        let mut sid = site.parent;
                        let offset;
                        loop {
                            let sid_actual =
                                sid.ok_or_else(|| format_err!("S_INLINESITE should have parent"))?;
                            let mut tmp_iter = info.symbols_at(sid_actual)?;
                            let data = tmp_iter.next()?.ok_or_else(|| {
                                format_err!("Unable to find symbol at {sid_actual}")
                            })?;
                            match data.parse()? {
                                SymbolData::Procedure(proc) => {
                                    offset = proc.offset;
                                    break;
                                }
                                SymbolData::InlineSite(isite) => {
                                    sid = isite.parent;
                                }
                                _ => bail!("Expected S_INLINESITE or procedure symbol at {sid_actual} but got {}", data.raw_kind()),
                            }
                        }

                        let (inlinee, fname) = inlinees.get(&site.inlinee).ok_or_else(|| {
                            format_err!("Cannot find inlinee for {}", site.inlinee)
                        })?;
                        // NOTE: BA_OP_ChangeCodeOffsetBase is actually wrong in pdb crate package
                        // See https://dev.azure.com/mseng/LLVM/_versionControl?path=%24/LLVM/pu/WinC/vctools/PDB/dia2/symcache.cpp&version=T&line=3453&lineEnd=3453&lineStartColumn=18&lineEndColumn=36&lineStyle=plain&_a=contents
                        // for what it should actually be. The value is actually an index into the
                        // nth S_SEPCODE entry which contains a section + offset value
                        let lines = inlinee.lines(offset, &site);
                        let proc_name = fname.to_string();
                        builder.update_from_iter(
                            &program,
                            &string_table,
                            &address_map,
                            &proc_name,
                            lines,
                        )?;
                    }
                    _ => {}
                }
            }
        }

        Ok(builder.build())
    }

    pub fn offset(&self, off: &usize) -> Option<impl Iterator<Item = &SrcLine>> {
        self.offset_to_line.get(off).map(|x| x.iter())
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
