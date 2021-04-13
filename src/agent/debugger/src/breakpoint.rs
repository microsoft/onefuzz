use anyhow::Result;
use win_util::process;
use winapi::um::winnt::HANDLE;

use crate::debugger::{BreakpointId, BreakpointType};

pub struct RvaExtraInfo {
    module: String,
    rva: u64,
}

impl RvaExtraInfo {
    pub fn module(&self) -> &str {
        &self.module
    }

    pub fn rva(&self) -> u64 {
        self.rva
    }
}

pub struct SymbolExtraInfo {
    module: String,
    function: String,
}

impl SymbolExtraInfo {
    pub fn module(&self) -> &str {
        &self.module
    }

    pub fn function(&self) -> &str {
        &self.function
    }
}

pub struct AddressExtraInfo {
    address: u64,
    original_byte: Option<u8>,
}

impl AddressExtraInfo {
    pub fn address(&self) -> u64 {
        self.address
    }
}

pub(crate) enum ExtraInfo {
    Rva(RvaExtraInfo),
    UnresolvedSymbol(SymbolExtraInfo),
    Resolved(AddressExtraInfo),
}

pub struct Breakpoint {
    id: BreakpointId,
    kind: BreakpointType,
    disabled: u32,
    hit_count: u64,
    extra_info: ExtraInfo,
}

impl Breakpoint {
    fn new(id: BreakpointId, kind: BreakpointType, detail: ExtraInfo) -> Self {
        Breakpoint {
            id,
            kind,
            disabled: 0,
            hit_count: 0,
            extra_info: detail,
        }
    }

    pub fn id(&self) -> BreakpointId {
        self.id
    }

    pub fn kind(&self) -> BreakpointType {
        self.kind
    }

    pub fn is_enabled(&self) -> bool {
        !self.is_disabled()
    }

    pub fn is_disabled(&self) -> bool {
        self.disabled > 0
    }

    pub(crate) fn disable(&mut self, process_handle: HANDLE) -> Result<()> {
        self.disabled = self.disabled.saturating_add(1);

        if self.is_disabled() {
            if let ExtraInfo::Resolved(AddressExtraInfo {
                address,
                original_byte: Some(original_byte),
            }) = &self.extra_info
            {
                write_instruction_byte(process_handle, *address, *original_byte)?;
            }
        }

        Ok(())
    }

    pub fn enable(&mut self, process_handle: HANDLE) -> Result<()> {
        self.disabled = self.disabled.saturating_sub(1);

        if let ExtraInfo::Resolved(AddressExtraInfo {
            address,
            original_byte,
        }) = &mut self.extra_info
        {
            let new_original_byte = process::read_memory(process_handle, *address as _)?;
            *original_byte = Some(new_original_byte);
            write_instruction_byte(process_handle, *address, 0xcc)?;
        }

        Ok(())
    }

    #[allow(unused)]
    pub fn hit_count(&self) -> u64 {
        self.hit_count
    }

    pub fn increment_hit_count(&mut self) {
        self.hit_count += 1;
    }

    pub(crate) fn extra_info(&self) -> &ExtraInfo {
        &self.extra_info
    }

    pub fn sym_extra_info(&self) -> Option<&SymbolExtraInfo> {
        if let ExtraInfo::UnresolvedSymbol(sym) = &self.extra_info {
            Some(sym)
        } else {
            None
        }
    }

    pub fn rva_extra_info(&self) -> Option<&RvaExtraInfo> {
        if let ExtraInfo::Rva(rva) = &self.extra_info {
            Some(rva)
        } else {
            None
        }
    }

    pub fn address_extra_info(&self) -> Option<&AddressExtraInfo> {
        if let ExtraInfo::Resolved(address) = &self.extra_info {
            Some(address)
        } else {
            None
        }
    }

    fn get_original_byte(&self) -> Option<u8> {
        if let ExtraInfo::Resolved(address) = &self.extra_info {
            address.original_byte
        } else {
            None
        }
    }

    fn set_original_byte(&mut self, byte: u8) {
        if let ExtraInfo::Resolved(address) = &mut self.extra_info {
            address.original_byte = Some(byte);
        }
    }

    pub fn from_symbol(
        id: BreakpointId,
        kind: BreakpointType,
        module: impl ToString,
        function: impl ToString,
    ) -> Self {
        let detail = ExtraInfo::UnresolvedSymbol(SymbolExtraInfo {
            module: module.to_string(),
            function: function.to_string(),
        });
        Breakpoint::new(id, kind, detail)
    }

    pub fn from_rva(
        id: BreakpointId,
        kind: BreakpointType,
        module: impl ToString,
        rva: u64,
    ) -> Self {
        let detail = ExtraInfo::Rva(RvaExtraInfo {
            module: module.to_string(),
            rva,
        });
        Breakpoint::new(id, kind, detail)
    }

    pub fn from_address(id: BreakpointId, kind: BreakpointType, address: u64) -> Self {
        let detail = ExtraInfo::Resolved(AddressExtraInfo {
            address,
            original_byte: None,
        });
        Breakpoint::new(id, kind, detail)
    }
}

pub(crate) struct BreakpointCollection {
    breakpoints: fnv::FnvHashMap<u64, Breakpoint>,
    min_breakpoint_addr: u64,
    max_breakpoint_addr: u64,
}

impl BreakpointCollection {
    pub fn new() -> Self {
        BreakpointCollection {
            breakpoints: fnv::FnvHashMap::default(),
            min_breakpoint_addr: u64::MAX,
            max_breakpoint_addr: u64::MIN,
        }
    }

    pub fn insert(&mut self, address: u64, breakpoint: Breakpoint) -> Option<Breakpoint> {
        self.min_breakpoint_addr = std::cmp::min(self.min_breakpoint_addr, address);
        self.max_breakpoint_addr = std::cmp::max(self.max_breakpoint_addr, address);
        self.breakpoints.insert(address, breakpoint)
    }

    pub fn contains_key(&self, address: u64) -> bool {
        self.breakpoints.contains_key(&address)
    }

    pub fn get_mut(&mut self, address: u64) -> Option<&mut Breakpoint> {
        self.breakpoints.get_mut(&address)
    }

    pub fn remove_all(&mut self, process_handle: HANDLE) -> Result<()> {
        for (address, breakpoint) in self.breakpoints.iter() {
            if let Some(original_byte) = breakpoint.get_original_byte() {
                write_instruction_byte(process_handle, *address, original_byte)?;
            }
        }

        Ok(())
    }

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
            if breakpoint.is_enabled() {
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
            if breakpoint.is_enabled() {
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
        process::write_memory_slice(process_handle, self.min_breakpoint_addr as _, &buffer)?;
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
