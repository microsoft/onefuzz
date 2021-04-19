// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    collections::btree_map::Range,
    fs,
    ops::RangeBounds,
    path::{Path, PathBuf},
};

use anyhow::Result;
use log::error;
use win_util::{file, handle::Handle};
use winapi::um::{
    handleapi::INVALID_HANDLE_VALUE,
    winnt::{HANDLE, IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE_I386},
};

use crate::{
    breakpoint::{BreakpointCollection, ResolvedBreakpoint},
    dbghelp,
    debugger::{BreakpointId, BreakpointType},
};

pub const UNKNOWN_MODULE_BASE_ADDRESS: u64 = u64::MAX;
pub const UNKNOWN_MODULE_NAME: &str = "*unknown module*";

pub struct Module {
    path: PathBuf,
    file_handle: Handle,
    base_address: u64,
    image_size: u32,
    machine: Machine,

    breakpoints: BreakpointCollection,

    // Track if we need to call SymLoadModule for the dll.
    sym_module_loaded: bool,
}

impl Module {
    pub fn new(module_handle: HANDLE, base_address: u64) -> Result<Self> {
        let path = file::get_path_from_handle(module_handle).unwrap_or_else(|e| {
            error!("Error getting path from file handle: {}", e);
            "???".into()
        });

        let image_details = get_image_details(&path)?;

        Ok(Module {
            path,
            file_handle: Handle(module_handle),
            base_address,
            image_size: image_details.image_size,
            machine: image_details.machine,
            sym_module_loaded: false,
            breakpoints: BreakpointCollection::new(),
        })
    }

    pub fn new_fake_module() -> Self {
        Module {
            path: UNKNOWN_MODULE_NAME.into(),
            file_handle: Handle(INVALID_HANDLE_VALUE),
            base_address: UNKNOWN_MODULE_BASE_ADDRESS,
            image_size: 0,
            machine: Machine::Unknown,
            breakpoints: BreakpointCollection::new(),
            sym_module_loaded: true,
        }
    }

    pub fn sym_load_module(&mut self, process_handle: HANDLE) -> Result<()> {
        if !self.sym_module_loaded {
            let dbghelp = dbghelp::lock()?;

            dbghelp.sym_load_module(
                process_handle,
                self.file_handle.0,
                &self.path,
                self.base_address,
                self.image_size,
            )?;

            self.sym_module_loaded = true;
        }

        Ok(())
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn base_address(&self) -> u64 {
        self.base_address
    }

    pub fn machine(&self) -> Machine {
        self.machine
    }

    #[allow(unused)]
    pub fn image_size(&self) -> u32 {
        self.image_size
    }

    pub fn name(&self) -> &Path {
        // Unwrap guaranteed by construction, we always have a filename.
        self.path.file_stem().unwrap().as_ref()
    }

    pub fn new_breakpoint(
        &mut self,
        id: BreakpointId,
        kind: BreakpointType,
        address: u64,
        process_handle: HANDLE,
    ) -> Result<()> {
        let mut breakpoint = ResolvedBreakpoint::new(id, kind, address);
        breakpoint.enable(process_handle)?;
        self.breakpoints.insert(address, breakpoint);
        Ok(())
    }

    #[allow(unused)]
    pub fn remove_breakpoints(&mut self, process_handle: HANDLE) -> Result<()> {
        if self.base_address == UNKNOWN_MODULE_BASE_ADDRESS {
            self.breakpoints.remove_all(process_handle)
        } else {
            self.breakpoints.bulk_remove_all(process_handle)
        }
    }

    pub fn apply_breakpoints(&mut self, process_handle: HANDLE) -> Result<()> {
        if self.base_address == UNKNOWN_MODULE_BASE_ADDRESS {
            self.breakpoints.apply_all(process_handle)
        } else {
            self.breakpoints.bulk_apply_all(process_handle)
        }
    }

    pub fn contains_breakpoint(&self, address: u64) -> bool {
        self.breakpoints.contains_key(address)
    }

    pub fn get_breakpoint_mut(&mut self, address: u64) -> Option<&mut ResolvedBreakpoint> {
        self.breakpoints.get_mut(address)
    }

    pub fn breakpoints_for_range(
        &self,
        range: impl RangeBounds<u64>,
    ) -> Range<u64, ResolvedBreakpoint> {
        self.breakpoints.breakpoints_for_range(range)
    }
}

#[derive(Copy, Clone, Debug, PartialEq)]
pub enum Machine {
    Unknown,
    X64,
    X86,
}

struct ImageDetails {
    image_size: u32,
    machine: Machine,
}

fn get_image_details(path: &Path) -> Result<ImageDetails> {
    let file = fs::File::open(path)?;
    let map = unsafe { memmap2::Mmap::map(&file)? };

    let header = goblin::pe::header::Header::parse(&map)?;
    let image_size = header
        .optional_header
        .map(|h| h.windows_fields.size_of_image)
        .ok_or_else(|| anyhow::anyhow!("Missing optional header in PE image"))?;

    let machine = match header.coff_header.machine {
        IMAGE_FILE_MACHINE_AMD64 => Machine::X64,
        IMAGE_FILE_MACHINE_I386 => Machine::X86,
        _ => Machine::Unknown,
    };

    Ok(ImageDetails {
        image_size,
        machine,
    })
}
