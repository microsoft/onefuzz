// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeSet;
use std::convert::TryInto;

use anyhow::{format_err, Result};
use goblin::pe::PE;
use iced_x86::{Decoder, DecoderOptions, Instruction, Mnemonic, OpKind};
use pdb::{
    AddressMap, DataSymbol, FallibleIterator, ProcedureSymbol, Rva, Source, SymbolData, PDB,
};

use crate::sancov::{SancovDelimiters, SancovTable};

/// Basic block offset provider for uninstrumented PE modules.
pub struct PeBasicBlockProvider {}

/// Basic block offset provider for Sancov-instrumented PE modules.
pub struct PeSancovBasicBlockProvider<'d, 'p, D> {
    data: &'p [u8],
    pe: &'p PE<'p>,
    pdb: &'p mut PDB<'d, D>,
}

impl<'d, 'p, D> PeSancovBasicBlockProvider<'d, 'p, D>
where
    D: Source<'d> + 'd,
{
    pub fn new(data: &'p [u8], pe: &'p PE<'p>, pdb: &'p mut PDB<'d, D>) -> Self {
        Self { data, pe, pdb }
    }

    /// Try to provide basic block offsets using available Sancov table symbols.
    ///
    /// If PC tables are available and definitely well-formed, use those
    /// directly. Otherwise, look for an inline counter or bool flag array, then
    /// disassemble all functions to reverse the instrumentation sites.
    pub fn provide(&mut self) -> Result<BTreeSet<u32>> {
        let mut visitor = SancovDelimiterVisitor::new(self.pdb.address_map()?);

        let global_symbols = self.pdb.global_symbols()?;
        let mut iter = global_symbols.iter();

        // Search symbols which delimit Sancov tables.
        while let Some(symbol) = iter.next()? {
            if let Ok(SymbolData::Data(data)) = symbol.parse() {
                visitor.visit_data_symbol(&data)?;
            }
        }

        // If we found a non-empty PC table, try to parse it.
        if let Some(pcs_table) = visitor.delimiters.pcs_table() {
            // Discovering and parsing the PC table can be error-prone, if we even have it. Mine it
            // for PCs if we can, with some strict assumptions. If we can't, fall back on reversing
            // the inline table accesses.
            if let Ok(blocks) = self.provide_from_pcs_table(pcs_table) {
                return Ok(blocks);
            }
        }

        // Either the PC table was empty, or something went wrong when parsing it.
        //
        // If we found any inline table, then we should still be able to reverse the instrumentation
        // sites by disassembling instructions that access the inline table region in expected ways.
        if let Some(inline_table) = visitor.delimiters.inline_table() {
            return self.provide_from_inline_table(inline_table);
        }

        anyhow::bail!("unable to find Sancov table")
    }

    // Search for instructions that access a known inline table region, and use their offsets to
    // reverse the instrumented basic blocks.
    fn provide_from_inline_table(&mut self, inline_table: SancovTable) -> Result<BTreeSet<u32>> {
        let mut visitor =
            SancovInlineAccessVisitor::new(inline_table, &self.data, &self.pe, &mut self.pdb)?;

        let debug_info = self.pdb.debug_information()?;
        let mut modules = debug_info.modules()?;

        while let Some(module) = modules.next()? {
            if let Some(module_info) = self.pdb.module_info(&module)? {
                let mut symbols = module_info.symbols()?;
                while let Some(symbol) = symbols.next()? {
                    if let Ok(SymbolData::Procedure(proc)) = symbol.parse() {
                        visitor.visit_procedure_symbol(&proc)?;
                    }
                }
            }
        }

        let global_symbols = self.pdb.global_symbols()?;
        let mut iter = global_symbols.iter();

        while let Some(symbol) = iter.next()? {
            if let Ok(SymbolData::Procedure(proc)) = symbol.parse() {
                visitor.visit_procedure_symbol(&proc)?;
            }
        }

        Ok(visitor.access_offsets)
    }

    // Try to parse instrumented VAs directly from the PC table.
    //
    // Currently this assumes `sizeof(uintptr_t) ==  8` for the target PE.
    fn provide_from_pcs_table(&mut self, pcs_table: SancovTable) -> Result<BTreeSet<u32>> {
        // Read the PE directly to extract the PCs from the PC table.
        let pe_alignment = self
            .pe
            .header
            .optional_header
            .ok_or_else(|| format_err!("PE file missing optional header"))?
            .windows_fields
            .file_alignment;
        let pe_offset = goblin::pe::utils::find_offset(
            pcs_table.offset as usize,
            &self.pe.sections,
            pe_alignment,
        );
        let pe_offset =
            pe_offset.ok_or_else(|| format_err!("could not find file offset for sancov table"))?;
        let table_range = pe_offset..(pe_offset + pcs_table.size);
        let pcs_table_data = self
            .data
            .get(table_range)
            .ok_or_else(|| format_err!("sancov table slice out of file range"))?;

        if pcs_table_data.len() % 16 != 0 {
            anyhow::bail!("invalid PC table size");
        }

        let mut pcs = BTreeSet::default();

        let module_base: u64 = self.pe.image_base.try_into()?;

        // Each entry is a struct with 2 `uintptr_t` values: a PC, then a flag.
        // We only want the PC, so start at 0 (the default) and step by 2 to
        // skip the flags.
        for chunk in pcs_table_data.chunks(8).step_by(2) {
            let le: [u8; 8] = chunk.try_into()?;
            let pc = u64::from_le_bytes(le);
            let pc_offset: u32 = pc
                .checked_sub(module_base)
                .ok_or_else(|| {
                    format_err!(
                        "underflow when computing offset from VA: {:x} - {:x}",
                        pc,
                        module_base,
                    )
                })?
                .try_into()?;
            pcs.insert(pc_offset);
        }

        Ok(pcs)
    }
}

/// Searches a PDB for data symbols that delimit various Sancov tables.
#[derive(Default)]
pub struct SancovDelimiterVisitor<'am> {
    address_map: AddressMap<'am>,
    delimiters: SancovDelimiters,
}

impl<'am> SancovDelimiterVisitor<'am> {
    pub fn new(address_map: AddressMap<'am>) -> Self {
        let delimiters = SancovDelimiters::default();

        Self { address_map, delimiters }
    }

    /// Visit a data symbol and check if it is a known Sancov delimiter. If it is, save its value.
    ///
    /// We want to visit all delimiter symbols, since we can only determine the redundant delimiters
    /// if we know that there are more compiler-specific variants present.
    pub fn visit_data_symbol(&mut self, data: &DataSymbol) -> Result<()> {
        let name = &*data.name.to_string();

        if let Ok(delimiter) = name.parse() {
            if let Some(Rva(offset)) = data.offset.to_rva(&self.address_map) {
                self.delimiters.insert(delimiter, offset);
            } else {
                log::error!("unable to map internal offset to RVA");
            }
        }

        Ok(())
    }
}

pub struct SancovInlineAccessVisitor<'d, 'p> {
    access_offsets: BTreeSet<u32>,
    address_map: AddressMap<'d>,
    data: &'p [u8],
    pe: &'p PE<'p>,
    table: SancovTable,
}

impl<'d, 'p> SancovInlineAccessVisitor<'d, 'p> {
    pub fn new<'pdb, D>(
        table: SancovTable,
        data: &'p [u8],
        pe: &'p PE<'p>,
        pdb: &'pdb mut PDB<'d, D>,
    ) -> Result<Self>
    where
        D: Source<'d> + 'd,
    {
        let access_offsets = BTreeSet::default();
        let address_map = pdb.address_map()?;
        Ok(Self {
            access_offsets,
            address_map,
            data,
            pe,
            table,
        })
    }

    pub fn visit_procedure_symbol(&mut self, proc: &ProcedureSymbol) -> Result<()> {
        let data = self.procedure_data(proc)?;

        let mut decoder = Decoder::new(64, data, DecoderOptions::NONE);

        // Set decoder IP to be an RVA.
        let proc_rip: u64 = proc.offset.to_rva(&self.address_map).unwrap().0.into();
        decoder.set_ip(proc_rip);

        let mut inst = Instruction::default();
        while decoder.can_decode() {
            decoder.decode_out(&mut inst);

            match inst.op_code().mnemonic() {
                Mnemonic::Add | Mnemonic::Inc => {
                    // These may be 8-bit counter updates, check further.
                }
                Mnemonic::Mov => {
                    // This may be a bool flag set or the start of an unoptimized
                    // 8-bit counter update sequence.
                    //
                    //     mov al, [rel <table>]
                    //
                    // or:
                    //
                    //     mov [rel <table>], 1
                    match (inst.op0_kind(), inst.op1_kind()) {
                        (OpKind::Register, OpKind::Memory) => {
                            // Possible start of an unoptimized 8-bit counter update sequence, like:
                            //
                            //     mov al, [rel <table>]
                            //     add al, 1
                            //     mov [rel <table>], al
                            //
                            // Check the operand sizes.

                            if inst.memory_size().size() != 1 {
                                // Load would span multiple table entries, skip.
                                continue;
                            }

                            if inst.op0_register().size() != 1 {
                                // Should be unreachable after a 1-byte load.
                                continue;
                            }
                        }
                        (OpKind::Memory, OpKind::Immediate8) => {
                            // Possible bool flag set, like:
                            //
                            //     mov [rel <table>], 1
                            //
                            // Check store size and immediate value.

                            if inst.memory_size().size() != 1 {
                                // Store would span multiple table entries, skip.
                                continue;
                            }

                            if inst.immediate8() != 1 {
                                // Not a bool flag set, skip.
                                continue;
                            }
                        }
                        _ => {
                            // Not a known update pattern, skip.
                            continue;
                        }
                    }
                }
                _ => {
                    // Does not correspond to any known counter update, so skip.
                    continue;
                }
            }

            if inst.is_ip_rel_memory_operand() {
                // When relative, `memory_displacement64()` returns a VA. The
                // decoder RIP is already set to be module image-relative, so
                // our "VA" is already an RVA.
                let accessed = inst.memory_displacement64() as u32;

                if self.table.range().contains(&accessed) {
                    self.access_offsets.insert(inst.ip() as u32);
                }
            }
        }

        Ok(())
    }

    fn procedure_data(&self, proc: &ProcedureSymbol) -> Result<&'p [u8]> {
        let alignment = self
            .pe
            .header
            .optional_header
            .ok_or_else(|| format_err!("PE file missing optional header"))?
            .windows_fields
            .file_alignment;

        let rva: usize = proc
            .offset
            .to_rva(&self.address_map)
            .ok_or_else(|| format_err!("unable to convert PDB offset to RVA"))?
            .0
            .try_into()?;

        let file_offset = goblin::pe::utils::find_offset(rva, &self.pe.sections, alignment)
            .ok_or_else(|| format_err!("unable to find PE offset for RVA"))?;

        let range = file_offset..(file_offset + proc.len as usize);

        self.data
            .get(range)
            .ok_or_else(|| format_err!("invalid PE file range for procedure data"))
    }
}
