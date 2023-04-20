// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::cell::{Ref, RefCell};
use std::collections::{BTreeMap, BTreeSet};
use std::io::Cursor;

use anyhow::Result;
use goblin::pe::PE;
use pdb::{AddressMap, ImageSectionHeader, PdbInternalSectionOffset, PDB};

use crate::debuginfo::{DebugInfo, Function};
use crate::path::FilePath;
use crate::{Address, Module, Offset};

impl<'data> Module<'data> for WindowsModule<'data> {
    fn executable_path(&self) -> &FilePath {
        &self.pe_path
    }

    fn debuginfo_path(&self) -> &FilePath {
        &self.pdb_path
    }

    fn read(&self, offset: Offset, size: u64) -> Result<&'data [u8]> {
        let size = usize::try_from(size)?;

        let start = self.translator.virtual_offset_to_file_offset(offset)?;
        let lo = usize::try_from(start)?;

        let end = lo.saturating_add(size);
        let ub = self.pe_data.len();
        let hi = usize::min(ub, end);

        Ok(&self.pe_data[lo..hi])
    }

    fn base_address(&self) -> Address {
        self.translator.base
    }

    fn executable_data(&self) -> &'data [u8] {
        self.pe_data
    }

    fn debuginfo_data(&self) -> &'data [u8] {
        self.pdb_data
    }

    fn debuginfo(&self) -> Result<DebugInfo> {
        use symbolic::debuginfo::Object;
        use symbolic::demangle::{Demangle, DemangleOptions};

        let extra = self.extra_debug_info()?;
        let opts = DemangleOptions::complete();

        let object = Object::parse(self.debuginfo_data())?;
        let session = object.debug_session()?;

        let mut functions = BTreeMap::new();

        for function in session.functions() {
            let function = function?;

            let name = function.name.try_demangle(opts).into_owned();
            let offset = Offset(function.address); // Misnamed.
            let size = function.size;
            let noreturn = extra.noreturns.contains(&offset);

            let f = Function {
                name,
                noreturn,
                offset,
                size,
            };
            functions.insert(offset, f);
        }

        Ok(DebugInfo::new(functions, Some(extra.labels)))
    }
}

pub struct WindowsModule<'data> {
    pe: PE<'data>,
    pe_data: &'data [u8],
    pe_path: FilePath,

    pdb: RefCell<PDB<'data, Cursor<&'data [u8]>>>,
    pdb_data: &'data [u8],
    pdb_path: FilePath,

    translator: Translator<'data>,
}

impl<'data> WindowsModule<'data> {
    pub fn new(
        pe_path: FilePath,
        pe_data: &'data [u8],
        pdb_path: FilePath,
        pdb_data: &'data [u8],
    ) -> Result<Self> {
        let pe = goblin::pe::PE::parse(pe_data)?;
        let mut pdb = pdb::PDB::open(Cursor::new(pdb_data))?;

        let base = Address(u64::try_from(pe.image_base)?);
        let translator = Translator::new(base, &mut pdb)?;

        let pdb = RefCell::new(pdb);

        Ok(Self {
            pe,
            pe_data,
            pe_path,
            pdb,
            pdb_data,
            pdb_path,
            translator,
        })
    }

    pub fn pe(&self) -> &PE<'data> {
        &self.pe
    }

    pub fn pdb(&self) -> Ref<PDB<'data, Cursor<&'data [u8]>>> {
        self.pdb.borrow()
    }

    fn extra_debug_info(&self) -> Result<ExtraDebugInfo> {
        use pdb::{FallibleIterator, SymbolData};

        let mut extra = ExtraDebugInfo::default();

        let mut pdb = self.pdb.borrow_mut();

        let di = pdb.debug_information()?;
        let mut modules = di.modules()?;

        while let Some(module) = modules.next()? {
            if let Some(mi) = pdb.module_info(&module)? {
                let mut symbols = mi.symbols()?;

                while let Some(symbol) = symbols.next()? {
                    #[allow(clippy::single_match)]
                    match symbol.parse() {
                        Ok(SymbolData::Procedure(proc)) => {
                            let noreturn = proc.flags.never;

                            if noreturn {
                                let internal = proc.offset;
                                let offset = self
                                    .translator
                                    .internal_section_offset_to_virtual_offset(internal)?;
                                extra.noreturns.insert(offset);
                            }
                        }
                        _ => {}
                    }
                }
            }
        }

        Ok(extra)
    }
}

#[derive(Default)]
struct ExtraDebugInfo {
    /// Jump targets, typically from `switch` cases.
    pub labels: BTreeSet<Offset>,

    /// Entry offsets functions that do not return.
    pub noreturns: BTreeSet<Offset>,
}

struct Translator<'data> {
    base: Address,
    address_map: AddressMap<'data>,
    sections: Vec<ImageSectionHeader>,
}

impl<'data> Translator<'data> {
    pub fn new<'a>(base: Address, pdb: &'a mut PDB<'data, Cursor<&'data [u8]>>) -> Result<Self> {
        let address_map = pdb.address_map()?;

        let sections = pdb
            .sections()?
            .ok_or_else(|| anyhow::anyhow!("error reading section headers"))?;

        Ok(Self {
            base,
            address_map,
            sections,
        })
    }

    pub fn virtual_offset_to_file_offset(&self, offset: Offset) -> Result<u32> {
        let internal = pdb::Rva(u32::try_from(offset.0)?)
            .to_internal_offset(&self.address_map)
            .ok_or_else(|| anyhow::anyhow!("could not map virtual offset to internal"))?;

        Ok(self.internal_section_offset_to_file_offset(internal))
    }

    pub fn internal_section_offset_to_file_offset(
        &self,
        internal: PdbInternalSectionOffset,
    ) -> u32 {
        let section_index = (internal.section - 1) as usize;
        let section = self.sections[section_index];

        let section_file_offset = section.pointer_to_raw_data;
        section_file_offset + internal.offset
    }

    pub fn internal_section_offset_to_virtual_offset(
        &self,
        internal: PdbInternalSectionOffset,
    ) -> Result<Offset> {
        let rva = internal
            .to_rva(&self.address_map)
            .ok_or_else(|| anyhow::anyhow!("no virtual offset for internal section offset"))?;

        Ok(Offset(u64::from(rva.0)))
    }
}
