// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    collections::hash_map,
    fs,
    path::{Path, PathBuf},
};

use anyhow::Result;
use log::{error, trace};
use win_util::{file, handle::Handle, last_os_error, process};
use winapi::{
    shared::minwindef::{DWORD, LPVOID},
    um::{
        processthreadsapi::{ResumeThread, SuspendThread},
        winbase::Wow64SuspendThread,
        winnt::{HANDLE, IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE_I386},
    },
};

use crate::{
    dbghelp::{self, FrameContext},
    debugger::{Breakpoint, BreakpointId, BreakpointType, ModuleBreakpoint, StepState},
};

struct ThreadInfo {
    id: u32,
    handle: HANDLE,
    suspended: bool,
    wow64: bool,
}

impl ThreadInfo {
    fn new(id: u32, handle: HANDLE, wow64: bool) -> Self {
        ThreadInfo {
            id,
            handle,
            wow64,
            suspended: false,
        }
    }

    fn resume_thread(&mut self) -> Result<()> {
        if !self.suspended {
            return Ok(());
        }

        let suspend_count = unsafe { ResumeThread(self.handle) };
        if suspend_count == (-1i32 as DWORD) {
            Err(last_os_error())
        } else {
            self.suspended = false;
            trace!("Resume {:x} - suspend_count: {}", self.id, suspend_count);
            Ok(())
        }
    }

    fn suspend_thread(&mut self) -> Result<()> {
        if self.suspended {
            return Ok(());
        }

        let suspend_count = if self.wow64 {
            unsafe { Wow64SuspendThread(self.handle) }
        } else {
            unsafe { SuspendThread(self.handle) }
        };

        if suspend_count == (-1i32 as DWORD) {
            Err(last_os_error())
        } else {
            self.suspended = true;
            trace!("Suspend {:x} - suspend_count: {}", self.id, suspend_count);
            Ok(())
        }
    }
}

#[derive(Clone)]
pub struct Module {
    path: PathBuf,
    file_handle: Handle,
    base_address: u64,
    image_size: u32,
    machine: Machine,

    // Track if we need to call SymLoadModule for the dll.
    sym_module_loaded: bool,
}

impl Module {
    fn new(module_handle: HANDLE, base_address: u64) -> Result<Self> {
        let path = file::get_path_from_handle(module_handle).unwrap_or_else(|e| {
            error!("Error getting path from file handle: {}", e);
            "???".into()
        });

        let image_details = get_image_details(&path)?;

        Ok(Self {
            path,
            file_handle: Handle(module_handle),
            base_address,
            image_size: image_details.image_size,
            machine: image_details.machine,
            sym_module_loaded: false,
        })
    }

    fn sym_load_module(&mut self, process_handle: HANDLE) -> Result<()> {
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

    pub fn image_size(&self) -> u32 {
        self.image_size
    }

    pub fn name(&self) -> &Path {
        // Unwrap guaranteed by construction, we always have a filename.
        self.path.file_stem().unwrap().as_ref()
    }
}

pub struct Target {
    process_id: DWORD,
    process_handle: HANDLE,
    current_thread_handle: HANDLE,
    current_thread_id: DWORD,
    saw_initial_bp: bool,
    saw_initial_wow64_bp: bool,
    wow64: bool,

    // Track if we need to call SymInitialize for the process and if we need to notify
    // dbghelp about loaded/unloaded dlls.
    sym_initialized: bool,
    exited: bool,

    thread_info: fnv::FnvHashMap<DWORD, ThreadInfo>,

    // We cache the current thread context for possible repeated queries and modifications.
    // We want to call GetThreadContext once, then call SetThreadContext (if necessary) before
    // resuming. Calling Get/Set/Get/Set doesn't seem to work because the second Get doesn't
    // see any the changes made in the Set call.
    current_context: Option<FrameContext>,

    // True if we need to set the thread context before resuming.
    context_is_modified: bool,

    // Key is base address (which also happens to be the HANDLE).
    modules: fnv::FnvHashMap<u64, Module>,

    breakpoints: fnv::FnvHashMap<u64, Breakpoint>,

    // Map of thread to stepping state (e.g. breakpoint address to restore breakpoints)
    single_step: fnv::FnvHashMap<HANDLE, StepState>,
}

impl Target {
    pub fn new(
        process_id: DWORD,
        thread_id: DWORD,
        process_handle: HANDLE,
        thread_handle: HANDLE,
    ) -> Self {
        let mut thread_handles = fnv::FnvHashMap::default();
        let wow64 = process::is_wow64_process(process_handle);
        thread_handles.insert(thread_id, ThreadInfo::new(thread_id, thread_handle, wow64));

        Self {
            process_id,
            current_thread_handle: thread_handle,
            current_thread_id: thread_id,
            process_handle,
            saw_initial_bp: false,
            saw_initial_wow64_bp: false,
            wow64,
            sym_initialized: false,
            exited: false,
            thread_info: thread_handles,
            current_context: None,
            context_is_modified: false,
            modules: fnv::FnvHashMap::default(),
            breakpoints: fnv::FnvHashMap::default(),
            single_step: fnv::FnvHashMap::default(),
        }
    }

    pub fn current_thread_handle(&self) -> HANDLE {
        self.current_thread_handle
    }

    pub fn current_thread_id(&self) -> DWORD {
        self.current_thread_id
    }

    pub fn create_new_thread(&mut self, thread_handle: HANDLE, thread_id: DWORD) {
        self.current_thread_handle = thread_handle;
        self.thread_info.insert(
            thread_id,
            ThreadInfo::new(thread_id, thread_handle, self.wow64),
        );
    }

    pub fn set_current_thread(&mut self, thread_id: DWORD) {
        self.current_thread_handle = self.thread_info.get(&thread_id).unwrap().handle;
    }

    pub fn exit_thread(&mut self, thread_id: DWORD) {
        self.thread_info.remove(&thread_id);
    }

    pub fn process_handle(&self) -> HANDLE {
        self.process_handle
    }

    pub fn process_id(&self) -> DWORD {
        self.process_id
    }

    pub fn saw_initial_wow64_bp(&self) -> bool {
        self.saw_initial_wow64_bp
    }

    pub fn set_saw_initial_wow64_bp(&mut self) {
        self.saw_initial_wow64_bp = true;
    }

    pub fn saw_initial_bp(&self) -> bool {
        self.saw_initial_bp
    }

    pub fn set_saw_initial_bp(&mut self) {
        self.saw_initial_bp = true;
    }

    pub fn exited(&self) -> bool {
        self.exited
    }

    pub fn modules(&self) -> hash_map::Iter<u64, Module> {
        self.modules.iter()
    }

    pub fn initial_bp(&mut self, load_symbols: bool) -> Result<()> {
        self.saw_initial_bp = true;

        if load_symbols || !self.breakpoints.is_empty() {
            self.sym_initialize()?;

            for (_, module) in self.modules.iter_mut() {
                if let Err(e) = module.sym_load_module(self.process_handle) {
                    error!("Error loading symbols: {}", e);
                }
            }
        }

        Ok(())
    }

    pub fn breakpoint_set_at_addr(&self, address: u64) -> bool {
        self.breakpoints.contains_key(&address)
    }

    pub(crate) fn single_step(&self, thread_handle: HANDLE) -> Option<StepState> {
        self.single_step.get(&thread_handle).cloned()
    }

    pub fn sym_initialize(&mut self) -> Result<()> {
        if !self.sym_initialized {
            let dbghelp = dbghelp::lock()?;
            if let Err(e) = dbghelp.sym_initialize(self.process_handle) {
                error!("Error in SymInitializeW: {}", e);

                if let Err(e) = dbghelp.sym_cleanup(self.process_handle) {
                    error!("Error in SymCleanup: {}", e);
                }

                return Err(e);
            }

            for (_, module) in self.modules.iter_mut() {
                if let Err(e) = module.sym_load_module(self.process_handle) {
                    error!(
                        "Error loading symbols for module {}: {}",
                        module.path.display(),
                        e
                    );
                }
            }

            self.sym_initialized = true;
        }

        Ok(())
    }

    /// Register the module loaded at `base_address`, returning the module name if the module
    /// is not a native dll in a wow64 process.
    pub fn load_module(
        &mut self,
        module_handle: HANDLE,
        base_address: u64,
    ) -> Result<Option<Module>> {
        let mut module = Module::new(module_handle, base_address)?;

        trace!(
            "Loading module {} at {:x}",
            module.name().display(),
            base_address
        );

        if module.machine == Machine::X64 && process::is_wow64_process(self.process_handle) {
            // We ignore native dlls in wow64 processes.
            return Ok(None);
        }

        if self.sym_initialized {
            if let Err(e) = module.sym_load_module(self.process_handle) {
                error!("Error loading symbols: {}", e);
            }
        }

        let base_address = module.base_address;
        if let Some(old_value) = self.modules.insert(base_address, module.clone()) {
            error!(
                "Existing module {} replace at base_address {}",
                old_value.path.display(),
                base_address
            );
        }

        Ok(Some(module))
    }

    pub fn unload_module(&mut self, base_address: u64) {
        // Drop the module and remove any breakpoints.
        if let Some(module) = self.modules.remove(&base_address) {
            let image_size = module.image_size as u64;
            self.breakpoints
                .retain(|&ip, _| ip < base_address || ip >= base_address + image_size);
        }
    }

    pub(crate) fn set_symbolic_breakpoint(
        &mut self,
        module_name: &str,
        func: &str,
        kind: BreakpointType,
        id: BreakpointId,
    ) -> Result<()> {
        match dbghelp::lock() {
            Ok(dbghelp) => match dbghelp.sym_from_name(self.process_handle, module_name, func) {
                Ok(sym) => {
                    self.apply_absolute_breakpoint(sym.address(), kind, id)?;
                }
                Err(_) => {
                    anyhow::bail!("unknown symbol {}!{}", module_name, func);
                }
            },
            Err(e) => {
                error!("Can't set symbolic breakpoints: {}", e);
            }
        }

        Ok(())
    }

    pub fn apply_absolute_breakpoint(
        &mut self,
        address: u64,
        kind: BreakpointType,
        id: BreakpointId,
    ) -> Result<()> {
        let original_byte: u8 = process::read_memory(self.process_handle, address as LPVOID)?;

        self.breakpoints
            .entry(address)
            .and_modify(|bp| {
                bp.set_kind(kind);
                bp.set_enabled(true);
                bp.set_original_byte(Some(original_byte));
                bp.set_id(id);
            })
            .or_insert(Breakpoint::new(
                address,
                kind,
                /*enabled*/ true,
                /*original_byte*/ Some(original_byte),
                /*hit_count*/ 0,
                id,
            ));

        write_instruction_byte(self.process_handle, address, 0xcc)?;

        Ok(())
    }

    pub(crate) fn apply_module_breakpoints(
        &mut self,
        base_address: u64,
        breakpoints: &[ModuleBreakpoint],
    ) -> Result<()> {
        if breakpoints.is_empty() {
            return Ok(());
        }

        // We want to set every breakpoint for the module at once. We'll read the just the
        // memory we need to do that, so find the min and max rva to compute how much memory
        // to read and update in the remote process.
        let (min, max) = breakpoints
            .iter()
            .fold((u64::max_value(), u64::min_value()), |acc, bp| {
                (acc.0.min(bp.rva()), acc.1.max(bp.rva()))
            });

        // Add 1 to include the final byte.
        let region_size = (max - min)
            .checked_add(1)
            .ok_or_else(|| anyhow::anyhow!("overflow in region size trying to set breakpoints"))?
            as usize;
        let remote_address = base_address.checked_add(min).ok_or_else(|| {
            anyhow::anyhow!("overflow in remote address calculation trying to set breakpoints")
        })? as LPVOID;

        let mut buffer: Vec<u8> = Vec::with_capacity(region_size);
        unsafe {
            buffer.set_len(region_size);
        }
        process::read_memory_array(self.process_handle, remote_address, &mut buffer[..])?;

        for mbp in breakpoints {
            let ip = base_address + mbp.rva();
            let offset = (mbp.rva() - min) as usize;

            trace!("Setting breakpoint at {:x}", ip);

            let bp = Breakpoint::new(
                ip,
                mbp.kind(),
                /*enabled*/ true,
                Some(buffer[offset]),
                /*hit_count*/ 0,
                mbp.id(),
            );

            buffer[offset] = 0xcc;

            self.breakpoints.insert(ip, bp);
        }

        process::write_memory_slice(self.process_handle, remote_address, &buffer[..])?;
        process::flush_instruction_cache(self.process_handle, remote_address, region_size)?;

        Ok(())
    }

    pub fn prepare_to_resume(&mut self) -> Result<()> {
        if let Some(context) = self.current_context.take() {
            if self.context_is_modified {
                context.set_thread_context(self.current_thread_handle)?;
            }
        }

        self.context_is_modified = false;

        Ok(())
    }

    fn ensure_current_context(&mut self) -> Result<()> {
        if self.current_context.is_none() {
            self.current_context = Some(dbghelp::get_thread_frame(
                self.process_handle,
                self.current_thread_handle,
            )?);
        }

        Ok(())
    }

    fn get_current_context(&mut self) -> Result<&FrameContext> {
        self.ensure_current_context()?;
        Ok(self.current_context.as_ref().unwrap())
    }

    fn get_current_context_mut(&mut self) -> Result<&mut FrameContext> {
        self.ensure_current_context()?;

        // Assume the caller will modify the context. When it is modified,
        // we must set it before resuming the target.
        self.context_is_modified = true;
        Ok(self.current_context.as_mut().unwrap())
    }

    pub fn read_register_u64(&mut self, reg: iced_x86::Register) -> Result<u64> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_register_u64(reg))
    }

    pub fn read_flags_register(&mut self) -> Result<u32> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_flags())
    }

    /// Handle a breakpoint that we set (as opposed to a breakpoint in user code, e.g.
    /// assertion.)
    ///
    /// Return the breakpoint id if it should be reported to the client.
    pub fn handle_breakpoint(&mut self, pc: u64) -> Result<Option<BreakpointId>> {
        enum HandleBreakpoint {
            User(BreakpointId, bool),
            StepOut(u64),
        }

        let handle_breakpoint = {
            let bp = self.breakpoints.get_mut(&pc).unwrap();

            bp.increment_hit_count();

            write_instruction_byte(self.process_handle, bp.ip(), bp.original_byte().unwrap())?;

            match bp.kind() {
                BreakpointType::OneTime => {
                    bp.set_enabled(false);
                    bp.set_original_byte(None);

                    // We are clearing the breakpoint after hitting it, so we do not need
                    // to single step.
                    HandleBreakpoint::User(bp.id(), false)
                }

                BreakpointType::Counter => {
                    // Single step so we can restore the breakpoint after stepping.
                    HandleBreakpoint::User(bp.id(), true)
                }

                BreakpointType::StepOut { rsp } => HandleBreakpoint::StepOut(rsp),
            }
        };

        let single_step = {
            let context = self.get_current_context_mut()?;
            context.set_program_counter(pc);

            // We need to single step if we need to restore the breakpoint.
            let single_step = match handle_breakpoint {
                HandleBreakpoint::User(_, single_step) => single_step,

                // Single step only when in a recursive call, which is inferred when the current
                // stack pointer (from context) is less then at the target to step out from (rsp).
                // Note this only works if the stack grows down.
                HandleBreakpoint::StepOut(rsp) => rsp > context.stack_pointer(),
            };

            if single_step {
                context.set_single_step(true);
                self.single_step
                    .insert(self.current_thread_handle, StepState::Breakpoint { pc });
            }

            single_step
        };

        let current_thread_handle = self.current_thread_handle;
        let context = self.get_current_context_mut()?;
        context.set_thread_context(current_thread_handle)?;
        self.context_is_modified = false;

        if single_step {
            for thread_info in self.thread_info.values_mut() {
                // Don't suspend any thread that we are single stepping. Typically
                // this is only the current thread/breakpoint, but debug events can be hit
                // on multiple threads at roughly the same time, so e.g. we may see a second
                // breakpoint from the OS before our scheduled single step exception.
                //
                // We must resume at least 1 thread when we are single stepping, but we can
                // safely resume all threads that are single stepping because we won't miss
                // any breakpoints as they are executing a single instruction.
                if self.single_step.contains_key(&thread_info.handle) {
                    thread_info.resume_thread()?;
                } else {
                    thread_info.suspend_thread()?;
                };
            }
        }

        Ok(match handle_breakpoint {
            HandleBreakpoint::User(id, _) => Some(id),
            HandleBreakpoint::StepOut(_) => None,
        })
    }

    pub(crate) fn handle_single_step(&mut self, step_state: StepState) -> Result<()> {
        self.single_step.remove(&self.current_thread_handle);

        match step_state {
            StepState::Breakpoint { pc } => {
                write_instruction_byte(self.process_handle, pc, 0xcc)?;

                // Resume all threads if we aren't waiting for any threads to single step.
                if self.single_step.is_empty() {
                    for thread_info in self.thread_info.values_mut() {
                        thread_info.resume_thread()?;
                    }
                }
            }
            _ => {}
        }

        Ok(())
    }

    pub fn prepare_to_step(&mut self) -> Result<bool> {
        // Don't change the reason we're single stepping on this thread if
        // we previously set the reason (e.g. so we would restore a breakpoint).
        self.single_step
            .entry(self.current_thread_handle)
            .or_insert(StepState::SingleStep);

        let context = self.get_current_context_mut()?;
        context.set_single_step(true);

        Ok(true)
    }

    pub fn set_exited(&mut self) -> Result<()> {
        self.exited = true;

        if self.sym_initialized {
            let dbghelp = dbghelp::lock()?;
            dbghelp.sym_cleanup(self.process_handle)?;
        }
        Ok(())
    }
}

#[derive(Copy, Clone, Debug, PartialEq)]
enum Machine {
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
    let map = unsafe { memmap::Mmap::map(&file)? };

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

fn write_instruction_byte(process_handle: HANDLE, ip: u64, b: u8) -> Result<()> {
    let orig_byte = [b; 1];
    let remote_address = ip as LPVOID;
    process::write_memory_slice(process_handle, remote_address, &orig_byte)?;
    process::flush_instruction_cache(process_handle, remote_address, orig_byte.len())?;
    Ok(())
}
