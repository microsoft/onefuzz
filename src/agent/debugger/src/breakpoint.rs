// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::uninit_vec)]

use std::{
    collections::{btree_map::Range, BTreeMap},
    ops::RangeBounds,
};

use anyhow::Result;
use win_util::process;
use winapi::um::winnt::HANDLE;

use crate::debugger::{BreakpointId, BreakpointType};

pub(crate) enum ExtraInfo {
    Rva(u64),
    Function(String),
}

pub(crate) struct UnresolvedBreakpoint {
    id: BreakpointId,
    kind: BreakpointType,
    module: String,
    extra_info: ExtraInfo,
}

impl UnresolvedBreakpoint {
    pub(crate) fn from_symbol(
        id: BreakpointId,
        kind: BreakpointType,
        module: impl ToString,
        function: impl ToString,
    ) -> Self {
        UnresolvedBreakpoint {
            id,
            kind,
            module: module.to_string(),
            extra_info: ExtraInfo::Function(function.to_string()),
        }
    }

    pub(crate) fn from_rva(
        id: BreakpointId,
        kind: BreakpointType,
        module: impl ToString,
        rva: u64,
    ) -> Self {
        UnresolvedBreakpoint {
            id,
            kind,
            module: module.to_string(),
            extra_info: ExtraInfo::Rva(rva),
        }
    }

    pub(crate) fn id(&self) -> BreakpointId {
        self.id
    }

    pub(crate) fn kind(&self) -> BreakpointType {
        self.kind
    }

    pub(crate) fn module(&self) -> &str {
        &self.module
    }

    pub(crate) fn extra_info(&self) -> &ExtraInfo {
        &self.extra_info
    }
}

pub struct ResolvedBreakpoint {
    id: BreakpointId,
    kind: BreakpointType,

    // We use a counter to handle multiple threads hitting the breakpoint at the same time.
    // Each thread will increment the disable count and the breakpoint won't be restored
    // until an equivalent number of threads enable the breakpoint.
    disabled: u32,
    hit_count: u64,
    address: u64,
    original_byte: Option<u8>,
}

impl ResolvedBreakpoint {
    pub fn new(id: BreakpointId, kind: BreakpointType, address: u64) -> Self {
        ResolvedBreakpoint {
            id,
            kind,
            disabled: 0,
            hit_count: 0,
            address,
            original_byte: None,
        }
    }

    pub fn id(&self) -> BreakpointId {
        self.id
    }

    pub fn kind(&self) -> BreakpointType {
        self.kind
    }

    pub fn update(&mut self, id: BreakpointId, kind: BreakpointType) {
        self.id = id;
        self.kind = kind;
    }

    #[allow(unused)]
    pub fn is_enabled(&self) -> bool {
        !self.is_disabled()
    }

    pub fn is_disabled(&self) -> bool {
        self.disabled > 0
    }

    fn is_applied(&self) -> bool {
        self.original_byte.is_some()
    }

    pub(crate) fn disable(&mut self, process_handle: HANDLE) -> Result<()> {
        self.disabled = self.disabled.saturating_add(1);

        if let Some(original_byte) = self.original_byte.take() {
            write_instruction_byte(process_handle, self.address, original_byte)?;
        }

        Ok(())
    }

    pub fn enable(&mut self, process_handle: HANDLE) -> Result<()> {
        self.disabled = self.disabled.saturating_sub(1);

        if self.original_byte.is_none() {
            self.original_byte = Some(process::read_memory(process_handle, self.address as _)?);
            write_instruction_byte(process_handle, self.address, 0xcc)?;
        }

        Ok(())
    }

    #[allow(unused)]
    pub fn hit_count(&self) -> u64 {
        self.hit_count
    }

    pub fn increment_hit_count(&mut self) {
        self.hit_count = self.hit_count.saturating_add(1);
    }

    pub(crate) fn get_original_byte(&self) -> Option<u8> {
        self.original_byte
    }

    fn set_original_byte(&mut self, byte: u8) {
        self.original_byte = Some(byte);
    }
}

pub(crate) struct BreakpointCollection {
    breakpoints: BTreeMap<u64, ResolvedBreakpoint>,
    min_breakpoint_addr: u64,
    max_breakpoint_addr: u64,
}

impl BreakpointCollection {
    pub fn new() -> Self {
        BreakpointCollection {
            breakpoints: BTreeMap::default(),
            min_breakpoint_addr: u64::MAX,
            max_breakpoint_addr: u64::MIN,
        }
    }

    pub fn insert(
        &mut self,
        address: u64,
        breakpoint: ResolvedBreakpoint,
    ) -> Option<ResolvedBreakpoint> {
        self.min_breakpoint_addr = std::cmp::min(self.min_breakpoint_addr, address);
        self.max_breakpoint_addr = std::cmp::max(self.max_breakpoint_addr, address);
        self.breakpoints.insert(address, breakpoint)
    }

    pub fn contains_key(&self, address: u64) -> bool {
        self.breakpoints.contains_key(&address)
    }

    pub fn get_mut(&mut self, address: u64) -> Option<&mut ResolvedBreakpoint> {
        self.breakpoints.get_mut(&address)
    }

    pub fn breakpoints_for_range(
        &self,
        range: impl RangeBounds<u64>,
    ) -> Range<u64, ResolvedBreakpoint> {
        self.breakpoints.range(range)
    }

    #[allow(unused)]
    pub fn remove_all(&mut self, process_handle: HANDLE) -> Result<()> {
        for (address, breakpoint) in self.breakpoints.iter() {
            if let Some(original_byte) = breakpoint.get_original_byte() {
                write_instruction_byte(process_handle, *address, original_byte)?;
            }
        }

        Ok(())
    }

    #[allow(unused)]
    pub fn bulk_remove_all(&mut self, process_handle: HANDLE) -> Result<()> {
        if self.breakpoints.is_empty() {
            return Ok(());
        }

        let mut buffer = self.bulk_read_process_memory(process_handle)?;

        for (address, breakpoint) in self.breakpoints.iter() {
            if let Some(original_byte) = breakpoint.get_original_byte() {
                let idx = (*address - self.min_breakpoint_addr) as usize;
                buffer[idx] = original_byte;
            }
        }

        self.bulk_write_process_memory(process_handle, &buffer)
    }

    pub fn apply_all(&mut self, process_handle: HANDLE) -> Result<()> {
        // No module, so we can't use the trick of reading and writing
        // a single large range of memory.
        for (address, breakpoint) in self.breakpoints.iter_mut() {
            if !breakpoint.is_applied() {
                let original_byte = process::read_memory(process_handle, *address as _)?;
                breakpoint.set_original_byte(original_byte);
                write_instruction_byte(process_handle, *address, 0xcc)?;
            }
        }

        Ok(())
    }

    pub fn bulk_apply_all(&mut self, process_handle: HANDLE) -> Result<()> {
        if self.breakpoints.is_empty() {
            return Ok(());
        }

        let mut buffer = self.bulk_read_process_memory(process_handle)?;

        for (address, breakpoint) in self.breakpoints.iter_mut() {
            if !breakpoint.is_applied() {
                let idx = (*address - self.min_breakpoint_addr) as usize;
                breakpoint.set_original_byte(buffer[idx]);
                buffer[idx] = 0xcc;
            }
        }

        self.bulk_write_process_memory(process_handle, &buffer)
    }

    fn bulk_region_size(&self) -> usize {
        (self.max_breakpoint_addr - self.min_breakpoint_addr + 1) as usize
    }

    fn bulk_read_process_memory(&self, process_handle: HANDLE) -> Result<Vec<u8>> {
        let mut buffer: Vec<u8> = Vec::with_capacity(self.bulk_region_size());
        unsafe {
            buffer.set_len(self.bulk_region_size());
        }
        process::read_memory_array(process_handle, self.min_breakpoint_addr as _, &mut buffer)?;
        Ok(buffer)
    }

    fn bulk_write_process_memory(&self, process_handle: HANDLE, buffer: &[u8]) -> Result<()> {
        process::write_memory_slice(process_handle, self.min_breakpoint_addr as _, buffer)?;
        process::flush_instruction_cache(
            process_handle,
            self.min_breakpoint_addr as _,
            self.bulk_region_size(),
        )?;
        Ok(())
    }
}

fn write_instruction_byte(process_handle: HANDLE, ip: u64, b: u8) -> Result<()> {
    let orig_byte = [b; 1];
    let remote_address = ip as _;
    process::write_memory_slice(process_handle, remote_address, &orig_byte)?;
    process::flush_instruction_cache(process_handle, remote_address, orig_byte.len())?;
    Ok(())
}
