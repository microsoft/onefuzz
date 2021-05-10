// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeSet;
use std::convert::{TryFrom, TryInto};

use anyhow::{format_err, Result};
use goblin::elf::{
    program_header::PT_LOAD, section_header::SectionHeader, sym::STT_NOTYPE, Elf, Sym,
};

use crate::sancov::{SancovDelimiters, SancovInlineAccessScanner, SancovTable};

#[derive(Clone, Copy, Debug)]
pub struct ElfContext<'d, 'e> {
    pub base: u64,
    pub data: &'d [u8],
    pub elf: &'e Elf<'e>,
}

impl<'d, 'e> ElfContext<'d, 'e> {
    pub fn new(data: &'d [u8], elf: &'e Elf<'e>) -> Result<Self> {
        // Find the virtual address of the lowest loadable segment.
        let base = elf
            .program_headers
            .iter()
            .filter(|h| h.p_type == PT_LOAD)
            .map(|h| h.p_vaddr)
            .min()
            .ok_or_else(|| format_err!("no loadable segments"))?;

        Ok(Self { base, data, elf })
    }

    pub fn try_symbol_name(&self, sym: &Sym) -> Result<String> {
        let name = self
            .elf
            .strtab
            .get(sym.st_name)
            .ok_or_else(|| format_err!("symbol index out of bounds: {}", sym.st_name))??
            .to_owned();

        Ok(name)
    }

    /// Convert a virtual address to an offset into the module's backing file.
    pub fn va_to_file_offset(&self, va: u64, section_index: Option<usize>) -> Result<usize> {
        let section = self.try_find_section_for_va(va, section_index)?;

        // VA of mapped section.
        let section_va = section.sh_addr;

        // Offset of `va` from the mapped section VA.
        let va_section_offset = va
            .checked_sub(section_va)
            .ok_or_else(|| format_err!("underflow computing virtual offset from section"))?;

        // The value of `va_section_offset` is the same in-memory and on-disk.
        // We calculated it using VAs, but we can apply it to the section's file
        // offset to get the file offset of the converted VA.
        let file_offset = section.sh_offset + va_section_offset;

        Ok(file_offset.try_into()?)
    }

    /// Convert a virtual address to a module-relative virtual memory offset.
    pub fn va_to_vm_offset(&self, va: u64) -> Result<u32> {
        let offset: u32 = va
            .checked_sub(self.base)
            .ok_or_else(|| {
                format_err!(
                    "underflow computing image offset: va = {:?}, base = {:x}",
                    va,
                    self.base,
                )
            })?
            .try_into()?;

        Ok(offset)
    }

    /// Try to find the section that contains the VA, if any.
    ///
    /// If passed an optional index to a section header which should contain the
    /// VA, try to resolve it and check the VM bounds.
    fn try_find_section_for_va(&self, va: u64, index: Option<usize>) -> Result<&SectionHeader> {
        // Convert for use with `SectionHeader::vm_range()`.
        let va: usize = va.try_into()?;

        let section = if let Some(index) = index {
            // If given an index, return the denoted section if it exists and contains the VA.
            let section = self
                .elf
                .section_headers
                .get(index)
                .ok_or_else(|| format_err!("section index out of bounds: {}", index))?;

            if !section.vm_range().contains(&va) {
                anyhow::bail!("VA not in section range: {:x}", va);
            }

            section
        } else {
            // If not given an index, try to find a containing section.
            self.elf
                .section_headers
                .iter()
                .find(|s| s.vm_range().contains(&va))
                .ok_or_else(|| format_err!("VA not contained in any section: {:x}", va))?
        };

        Ok(section)
    }
}

pub struct ElfSancovBasicBlockProvider<'d, 'e> {
    ctx: ElfContext<'d, 'e>,
    check_pc_table: bool,
}

impl<'d, 'e> ElfSancovBasicBlockProvider<'d, 'e> {
    pub fn new(ctx: ElfContext<'d, 'e>) -> Self {
        let check_pc_table = true;
        Self {
            check_pc_table,
            ctx,
        }
    }

    pub fn set_check_pc_table(&mut self, check: bool) {
        self.check_pc_table = check;
    }

    pub fn provide(&mut self) -> Result<BTreeSet<u32>> {
        let mut visitor = DelimiterVisitor::new(self.ctx);

        for sym in self.ctx.elf.syms.iter() {
            if let STT_NOTYPE = sym.st_type() {
                visitor.visit_data_symbol(sym)?;
            }
        }

        if self.check_pc_table {
            if let Some(pcs_table) = visitor.delimiters.pcs_table(false) {
                if let Ok(blocks) = self.provide_from_pcs_table(pcs_table) {
                    return Ok(blocks);
                }
            }
        }

        if let Some(inline_table) = visitor.delimiters.inline_table(false) {
            return self.provide_from_inline_table(inline_table);
        }

        anyhow::bail!("unable to find Sancov table")
    }

    pub fn provide_from_inline_table(
        &mut self,
        inline_table: SancovTable,
    ) -> Result<BTreeSet<u32>> {
        let mut visitor = InlineAccessVisitor::new(inline_table, self.ctx);

        for sym in self.ctx.elf.syms.iter() {
            visitor.visit_symbol(&sym)?;
        }

        Ok(visitor.scanner.offsets)
    }

    pub fn provide_from_pcs_table(&mut self, pcs_table: SancovTable) -> Result<BTreeSet<u32>> {
        let vm_offset: u64 = pcs_table.offset.into();
        let va = self.ctx.base + vm_offset;
        let file_offset = self.ctx.va_to_file_offset(va, None)?;
        let file_range = file_offset..(file_offset + pcs_table.size);

        let table_data = self
            .ctx
            .data
            .get(file_range)
            .ok_or_else(|| format_err!("Sancov table data out of file range"))?;

        // Assumes x86-64, `sizeof(uintptr_t) == 8`.
        //
        // Should check if `e_ident[EI_CLASS]` is `ELFCLASS32` or `ELFCLASS64`,
        // or equivalently, `elf.is_64`.
        if table_data.len() % 16 != 0 {
            anyhow::bail!("invalid PC table size");
        }

        let mut pcs = BTreeSet::default();

        // Each entry is a struct with 2 `uintptr_t` values: a PC, then a flag.
        // We only want the PC, so start at 0 (the default) and step by 2 to
        // skip the flags.
        for chunk in table_data.chunks(8).step_by(2) {
            let le: [u8; 8] = chunk.try_into()?;
            let pc = u64::from_le_bytes(le);
            let pc_vm_offset = self.ctx.va_to_vm_offset(pc)?;
            pcs.insert(pc_vm_offset);
        }

        Ok(pcs)
    }
}

struct DelimiterVisitor<'d, 'e> {
    ctx: ElfContext<'d, 'e>,
    delimiters: SancovDelimiters,
}

impl<'d, 'e> DelimiterVisitor<'d, 'e> {
    pub fn new(ctx: ElfContext<'d, 'e>) -> Self {
        let delimiters = SancovDelimiters::default();

        Self { ctx, delimiters }
    }

    pub fn visit_data_symbol(&mut self, sym: Sym) -> Result<()> {
        let va = sym.st_value;

        if va == 0 {
            return Ok(());
        }

        let offset = self.ctx.va_to_vm_offset(va)?;
        let name = self.ctx.try_symbol_name(&sym)?;

        if let Ok(delimiter) = name.parse() {
            self.delimiters.insert(delimiter, offset);
        }

        Ok(())
    }
}

pub struct InlineAccessVisitor<'d, 'e> {
    ctx: ElfContext<'d, 'e>,
    scanner: SancovInlineAccessScanner,
}

impl<'d, 'e> InlineAccessVisitor<'d, 'e> {
    pub fn new(table: SancovTable, ctx: ElfContext<'d, 'e>) -> Self {
        let scanner = SancovInlineAccessScanner::new(ctx.base, table);

        Self { ctx, scanner }
    }

    pub fn visit_symbol(&mut self, sym: &Sym) -> Result<()> {
        if sym.st_size == 0 {
            return Ok(());
        }

        if !sym.is_function() {
            return Ok(());
        }

        if sym.is_import() {
            return Ok(());
        }

        let va = sym.st_value;

        let file_range = {
            let index = sym.st_shndx.into();
            let lo: usize = self.ctx.va_to_file_offset(va, index)?;
            let hi: usize = lo + usize::try_from(sym.st_size)?;
            lo..hi
        };
        let data = self
            .ctx
            .data
            .get(file_range)
            .ok_or_else(|| format_err!("procedure out of data bounds"))?;

        self.scanner.scan(data, va)?;

        Ok(())
    }
}
