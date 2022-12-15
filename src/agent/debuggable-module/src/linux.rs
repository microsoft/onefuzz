// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};
use std::ops::Range;

use anyhow::{bail, Result};
use gimli::{EndianSlice, LittleEndian, SectionId};
use goblin::elf::{program_header::PT_LOAD, Elf, ProgramHeader};

use crate::debuginfo::{DebugInfo, Function};
use crate::path::FilePath;
use crate::{Address, Module, Offset};

impl<'data> Module<'data> for LinuxModule<'data> {
    fn executable_path(&self) -> &FilePath {
        &self.path
    }

    fn debuginfo_path(&self) -> &FilePath {
        &self.path
    }

    fn read(&self, offset: Offset, size: u64) -> Result<&'data [u8]> {
        if size == 0 {
            return Ok(&[]);
        }

        let addr = Address(self.vmmap.base()).offset_by(offset)?.0;

        for segment in self.vmmap.segments.values() {
            if segment.vm_range.contains(&addr) {
                // Segment-relative offset of the virtual address.
                let seg_off = addr.saturating_sub(segment.vm_range.start);

                let file_offset = segment.file_range.start + seg_off;
                let available = segment.file_size().saturating_sub(seg_off);
                let read_size = u64::min(available, size);

                let lo = file_offset as usize;
                let hi = file_offset.saturating_add(read_size) as usize;

                return Ok(&self.data[lo..hi]);
            }
        }

        bail!("no data for VM offset: {:x}", offset.0);
    }

    fn base_address(&self) -> Address {
        Address(self.vmmap.base())
    }

    fn executable_data(&self) -> &'data [u8] {
        self.data
    }

    fn debuginfo_data(&self) -> &'data [u8] {
        // Assume embedded DWARF.
        self.data
    }

    fn debuginfo(&self) -> Result<DebugInfo> {
        use symbolic::debuginfo::Object;
        use symbolic::demangle::{Demangle, DemangleOptions};

        let noreturns = self.noreturns()?;
        let opts = DemangleOptions::complete();

        let object = Object::parse(self.debuginfo_data())?;
        let session = object.debug_session()?;

        let mut functions = BTreeMap::new();

        for function in session.functions() {
            let function = function?;

            let name = function.name.try_demangle(opts).into_owned();
            let offset = Offset(function.address); // Misnamed.
            let size = function.size;
            let noreturn = noreturns.contains(&offset);

            let f = Function {
                name,
                noreturn,
                offset,
                size,
            };
            functions.insert(offset, f);
        }

        Ok(DebugInfo::new(functions, None))
    }
}

pub struct LinuxModule<'data> {
    path: FilePath,
    data: &'data [u8],
    elf: Elf<'data>,
    vmmap: VmMap,
}

impl<'data> LinuxModule<'data> {
    pub fn new(path: FilePath, data: &'data [u8]) -> Result<Self> {
        let elf = Elf::parse(data)?;
        let vmmap = VmMap::new(&elf)?;

        Ok(Self {
            path,
            data,
            elf,
            vmmap,
        })
    }

    pub fn elf(&self) -> &Elf<'data> {
        &self.elf
    }

    fn noreturns(&self) -> Result<BTreeSet<Offset>> {
        use gimli::{AttributeValue, DW_AT_low_pc, DW_AT_noreturn, DW_TAG_subprogram, Dwarf};

        let loader = |s| self.load_section(s);
        let dwarf = Dwarf::load(loader)?;

        let mut noreturns = BTreeSet::new();

        // Iterate over all compilation units.
        let mut headers = dwarf.units();
        while let Some(header) = headers.next()? {
            let unit = dwarf.unit(header)?;

            let mut entries = unit.entries();
            while let Some((_, entry)) = entries.next_dfs()? {
                // Look for `noreturn` functions.
                if entry.tag() == DW_TAG_subprogram {
                    let mut low_pc = None;

                    // Find the virtual function low_pc offset.
                    let mut attrs = entry.attrs();
                    while let Some(attr) = attrs.next()? {
                        if attr.name() == DW_AT_low_pc {
                            let value = attr.value();

                            if let AttributeValue::Addr(value) = value {
                                // `low_pc` is 0 for inlines.
                                if value != 0 {
                                    low_pc = Some(value);
                                    break;
                                }
                            }
                        }
                    }

                    let low_pc = if let Some(low_pc) = low_pc {
                        low_pc
                    } else {
                        // No low PC, can't locate subprogram.
                        continue;
                    };

                    let mut attrs = entry.attrs();
                    while let Some(attr) = attrs.next()? {
                        if attr.name() == DW_AT_noreturn {
                            let value = attr.value();

                            if let AttributeValue::Flag(is_set) = value {
                                if is_set {
                                    let base = Address(self.vmmap.base());
                                    let offset = Address(low_pc).offset_from(base)?;
                                    noreturns.insert(offset);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        Ok(noreturns)
    }

    fn load_section(&self, section: SectionId) -> Result<EndianSlice<LittleEndian>> {
        for shdr in &self.elf.section_headers {
            if let Some(name) = self.elf.shdr_strtab.get_at(shdr.sh_name) {
                if name == section.name() {
                    if let Some(range) = shdr.file_range() {
                        if let Some(data) = self.data.get(range) {
                            let data = EndianSlice::new(data, LittleEndian);
                            return Ok(data);
                        }
                    }
                }
            }
        }

        let data = EndianSlice::new(&[], LittleEndian);
        Ok(data)
    }
}

struct VmMap {
    base: u64,
    segments: BTreeMap<u64, Segment>,
}

impl VmMap {
    pub fn new(elf: &Elf) -> Result<Self> {
        let mut segments = BTreeMap::new();

        for header in &elf.program_headers {
            if header.p_type == PT_LOAD {
                let segment = Segment::from(header);
                segments.insert(segment.base(), segment);
            }
        }

        let base = *segments.keys().next().unwrap();

        Ok(Self { base, segments })
    }

    pub fn base(&self) -> u64 {
        self.base
    }
}

#[derive(Clone, Debug)]
pub struct Segment {
    vm_range: Range<u64>, // VM range may exceed file range.
    file_range: Range<u64>,
}

impl Segment {
    pub fn base(&self) -> u64 {
        self.vm_range.start
    }

    pub fn vm_size(&self) -> u64 {
        self.vm_range.end - self.vm_range.start
    }

    pub fn file_size(&self) -> u64 {
        self.file_range.end - self.file_range.start
    }
}

impl<'h> From<&'h ProgramHeader> for Segment {
    fn from(header: &'h ProgramHeader) -> Self {
        let file_range = {
            let lo = header.p_offset;
            let hi = lo.saturating_add(header.p_filesz);
            lo..hi
        };

        let vm_range = {
            let lo = header.p_vaddr;
            let hi = lo.saturating_add(header.p_memsz);
            lo..hi
        };

        Self {
            file_range,
            vm_range,
        }
    }
}
