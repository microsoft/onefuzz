// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::single_match)]

use std::{io, num::NonZeroU64, path::Path};

use anyhow::{format_err, Result};
use log::{debug, error, trace};
use rand::{thread_rng, Rng};
use win_util::process;
use winapi::{
    shared::{
        minwindef::{DWORD, LPCVOID},
        winerror::ERROR_ACCESS_DENIED,
    },
    um::{
        processthreadsapi::{ResumeThread, SuspendThread},
        winbase::Wow64SuspendThread,
        winnt::HANDLE,
    },
};

use crate::{
    breakpoint::{self, ResolvedBreakpoint, UnresolvedBreakpoint},
    dbghelp::{self, FrameContext},
    debugger::{BreakpointId, BreakpointType, ModuleLoadInfo},
    module::{self, Machine, Module},
};

#[derive(Copy, Clone)]
pub(crate) enum StepState {
    RestoreBreakpointAfterStep { pc: u64 },
    SingleStep,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ThreadState {
    Runnable,
    Suspended,
    Exited,
}

struct ThreadInfo {
    #[allow(unused)]
    id: u32,
    handle: HANDLE,
    state: ThreadState,
    wow64: bool,
}

const SUSPEND_RESUME_ERROR_CODE: DWORD = -1i32 as DWORD;

impl ThreadInfo {
    fn new(id: u32, handle: HANDLE, wow64: bool) -> Self {
        ThreadInfo {
            id,
            handle,
            wow64,
            state: ThreadState::Runnable,
        }
    }

    fn resume_thread(&mut self) -> Result<ThreadState> {
        if self.state == ThreadState::Runnable {
            return Ok(self.state);
        }

        let prev_suspend_count = unsafe { ResumeThread(self.handle) };

        match prev_suspend_count {
            SUSPEND_RESUME_ERROR_CODE => {
                let os_error = io::Error::last_os_error();

                if Self::is_os_error_from_exited_thread(&os_error)? {
                    self.state = ThreadState::Exited;
                } else {
                    return Err(os_error.into());
                }
            }
            0 => {
                // No-op: thread was runnable, and is still runnable.
                self.state = ThreadState::Runnable;
            }
            1 => {
                // Was suspended, now runnable.
                self.state = ThreadState::Runnable;
            }
            _ => {
                // Previous suspend count > 1. Was suspended, still is.
                self.state = ThreadState::Suspended;
            }
        }

        Ok(self.state)
    }

    fn suspend_thread(&mut self) -> Result<ThreadState> {
        if self.state == ThreadState::Suspended {
            return Ok(self.state);
        }

        let prev_suspend_count = if self.wow64 {
            unsafe { Wow64SuspendThread(self.handle) }
        } else {
            unsafe { SuspendThread(self.handle) }
        };

        match prev_suspend_count {
            SUSPEND_RESUME_ERROR_CODE => {
                let os_error = io::Error::last_os_error();

                if Self::is_os_error_from_exited_thread(&os_error)? {
                    self.state = ThreadState::Exited;
                } else {
                    return Err(os_error.into());
                }
            }
            _ => {
                // Suspend count was incremented. Even if the matched value is 0, it means
                // the current suspend count is 1, and the thread is suspended.
                self.state = ThreadState::Suspended;
            }
        }

        Ok(self.state)
    }

    fn is_os_error_from_exited_thread(os_error: &io::Error) -> Result<bool> {
        let raw_os_error = os_error
            .raw_os_error()
            .ok_or_else(|| format_err!("OS error missing raw error"))?;

        let exited = match raw_os_error as DWORD {
            ERROR_ACCESS_DENIED => {
                // Assume, as a debugger, we always have the `THREAD_SUSPEND_RESUME`
                // access right, and thus we should interpret this error to mean that
                // the thread has exited.
                //
                // This means we are observing a race between OS-level thread exit and
                // the (pending) debug event.
                true
            }
            _ => false,
        };

        Ok(exited)
    }
}

#[derive(Copy, Clone, PartialEq)]
enum SymInitalizeState {
    NotInitialized,
    InitializeNeeded,
    Initialized,
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
    sym_initialize_state: SymInitalizeState,
    exited: bool,

    // Map of thread ID to thread info.
    thread_info: fnv::FnvHashMap<DWORD, ThreadInfo>,

    // We cache the current thread context for possible repeated queries and modifications.
    // We want to call GetThreadContext once, then call SetThreadContext (if necessary) before
    // resuming. Calling Get/Set/Get/Set doesn't seem to work because the second Get doesn't
    // see any the changes made in the Set call.
    current_context: Option<FrameContext>,

    // Key is base address (which also happens to be the HANDLE).
    modules: fnv::FnvHashMap<u64, Module>,

    // Breakpoints that are not yet resolved to a virtual address, so either an RVA or symbol.
    unresolved_breakpoints: Vec<UnresolvedBreakpoint>,

    // Map of thread ID to stepping state (e.g. breakpoint address to restore breakpoints)
    single_step: fnv::FnvHashMap<DWORD, StepState>,

    // When stepping after hitting a breakpoint, we need to restore the breakpoint.
    // We track the address of the breakpoint to restore. 1 is sufficient because we
    // can only hit a single breakpoint between calls to where we restore the breakpoint.
    restore_breakpoint_pc: Option<u64>,
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
            sym_initialize_state: SymInitalizeState::NotInitialized,
            exited: false,
            thread_info: thread_handles,
            current_context: None,
            modules: fnv::FnvHashMap::default(),
            unresolved_breakpoints: vec![],
            single_step: fnv::FnvHashMap::default(),
            restore_breakpoint_pc: None,
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
        self.current_thread_id = thread_id;
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

    pub fn saw_initial_bp(&self) -> bool {
        self.saw_initial_bp
    }

    pub fn exited(&self) -> bool {
        self.exited
    }

    pub fn initial_bp(&mut self) -> Result<()> {
        self.saw_initial_bp = true;

        if !self.unresolved_breakpoints.is_empty() {
            self.maybe_sym_initialize()?;
            self.try_resolve_all_unresolved_breakpoints();
            for module in self.modules.values_mut() {
                module.apply_breakpoints(self.process_handle)?;
            }
        }

        Ok(())
    }

    pub fn initial_wow64_bp(&mut self) {
        self.saw_initial_wow64_bp = true;
    }

    fn try_resolve_all_unresolved_breakpoints(&mut self) {
        // borrowck - take ownership from self so we call `try_resolve_unresolved_breakpoint`.
        let mut unresolved_breakpoints = std::mem::take(&mut self.unresolved_breakpoints);
        unresolved_breakpoints.retain(|bp| match self.try_resolve_unresolved_breakpoint(bp) {
            Ok(resolved) => !resolved,
            Err(err) => {
                debug!("Error resolving breakpoint: {:?}", err);
                true
            }
        });
        assert!(self.unresolved_breakpoints.is_empty());
        self.unresolved_breakpoints = unresolved_breakpoints;
    }

    /// Try to resolve a single unresolved breakpoint, returning true if the breakpoint
    /// was successfully resolved.
    fn try_resolve_unresolved_breakpoint(
        &mut self,
        breakpoint: &UnresolvedBreakpoint,
    ) -> Result<bool> {
        if !self.saw_initial_bp {
            return Ok(false);
        }

        let process_handle = self.process_handle; // borrowck
        let mut resolved = false;
        match breakpoint.extra_info() {
            breakpoint::ExtraInfo::Rva(rva) => {
                if let Some(module) = self.module_from_name_mut(breakpoint.module()) {
                    let address = module.base_address() + *rva;
                    module.new_breakpoint(
                        breakpoint.id(),
                        breakpoint.kind(),
                        address,
                        process_handle,
                    )?;
                    resolved = true;
                }
            }
            breakpoint::ExtraInfo::Function(func) => {
                if let Some(module) = self.module_from_name_mut(breakpoint.module()) {
                    match dbghelp::lock() {
                        Ok(dbghelp) => {
                            match dbghelp.sym_from_name(process_handle, module.name(), func) {
                                Ok(sym) => {
                                    module.new_breakpoint(
                                        breakpoint.id(),
                                        breakpoint.kind(),
                                        sym.address(),
                                        process_handle,
                                    )?;
                                    resolved = true;
                                }
                                Err(_) => {
                                    debug!("unknown symbol {}!{}", module.name().display(), func);
                                }
                            }
                        }
                        Err(e) => {
                            error!("Can't set symbolic breakpoints: {:?}", e);
                        }
                    }
                }
            }
        }

        Ok(resolved)
    }

    pub fn new_symbolic_breakpoint(
        &mut self,
        id: BreakpointId,
        module: impl AsRef<str>,
        func: impl AsRef<str>,
        kind: BreakpointType,
    ) -> Result<BreakpointId> {
        self.maybe_sym_initialize()?;
        let bp = UnresolvedBreakpoint::from_symbol(id, kind, module.as_ref(), func.as_ref());
        if !self.try_resolve_unresolved_breakpoint(&bp)? {
            self.unresolved_breakpoints.push(bp);
        }
        Ok(id)
    }

    pub fn new_rva_breakpoint(
        &mut self,
        id: BreakpointId,
        module: impl AsRef<str>,
        rva: u64,
        kind: BreakpointType,
    ) -> Result<BreakpointId> {
        let bp = UnresolvedBreakpoint::from_rva(id, kind, module.as_ref(), rva);
        if !self.try_resolve_unresolved_breakpoint(&bp)? {
            self.unresolved_breakpoints.push(bp);
        }
        Ok(id)
    }

    pub fn new_absolute_breakpoint(
        &mut self,
        id: BreakpointId,
        address: u64,
        kind: BreakpointType,
    ) -> Result<BreakpointId> {
        let process_handle = self.process_handle; // borrowck
        self.module_from_address(address)
            .new_breakpoint(id, kind, address, process_handle)?;
        Ok(id)
    }

    fn module_base_from_address(&self, address: u64) -> Result<NonZeroU64> {
        let dbghelp = dbghelp::lock().expect("can't lock dbghelp to find module");
        dbghelp.get_module_base(self.process_handle, address)
    }

    fn module_from_address(&mut self, address: u64) -> &mut Module {
        let module_base = self
            .module_base_from_address(address)
            .unwrap_or(unsafe { NonZeroU64::new_unchecked(module::UNKNOWN_MODULE_BASE_ADDRESS) });

        self.modules
            .entry(module_base.get())
            .or_insert_with(Module::new_fake_module)
    }

    fn module_from_name_mut(&mut self, name: &str) -> Option<&mut Module> {
        let name = Path::new(name);
        self.modules
            .values_mut()
            .find(|module| module.name() == name)
    }

    fn get_breakpoint_for_address(&mut self, address: u64) -> Option<&mut ResolvedBreakpoint> {
        self.module_from_address(address)
            .get_breakpoint_mut(address)
    }

    pub fn breakpoint_set_at_addr(&mut self, address: u64) -> bool {
        self.module_from_address(address)
            .contains_breakpoint(address)
    }

    pub(crate) fn expecting_single_step(&self, thread_id: DWORD) -> bool {
        self.single_step.contains_key(&thread_id)
    }

    pub(crate) fn complete_single_step(&mut self, thread_id: DWORD) -> Result<()> {
        // We now re-enable the breakpoint so that the next time we step, the breakpoint
        // will be restored.
        if let Some(restore_breakpoint_pc) = self.restore_breakpoint_pc.take() {
            let process_handle = self.process_handle; // borrowck
            if let Some(breakpoint) = self.get_breakpoint_for_address(restore_breakpoint_pc) {
                trace!("Restoring breakpoint at 0x{:x}", restore_breakpoint_pc);
                breakpoint.enable(process_handle)?;
            }
        }

        self.single_step.remove(&thread_id);

        Ok(())
    }

    pub fn maybe_sym_initialize(&mut self) -> Result<()> {
        if self.sym_initialize_state == SymInitalizeState::Initialized {
            return Ok(());
        }

        if self.sym_initialize_state == SymInitalizeState::NotInitialized {
            self.sym_initialize_state = SymInitalizeState::InitializeNeeded;
        }

        if self.saw_initial_bp && self.sym_initialize_state == SymInitalizeState::InitializeNeeded {
            self.sym_initialize()?;
            self.sym_initialize_state = SymInitalizeState::Initialized;
        }

        Ok(())
    }

    fn sym_initialize(&mut self) -> Result<()> {
        let dbghelp = dbghelp::lock()?;
        if let Err(e) = dbghelp.sym_initialize(self.process_handle) {
            error!("Error in SymInitializeW: {:?}", e);

            if let Err(e) = dbghelp.sym_cleanup(self.process_handle) {
                error!("Error in SymCleanup: {:?}", e);
            }

            return Err(e);
        }

        for (_, module) in self.modules.iter_mut() {
            if let Err(e) = module.sym_load_module(self.process_handle) {
                error!(
                    "Error loading symbols for module {}: {:?}",
                    module.path().display(),
                    e
                );
            }
        }

        Ok(())
    }

    /// Register the module loaded at `base_address`, returning the module name if the module
    /// is not a native dll in a wow64 process.
    pub fn load_module(
        &mut self,
        module_handle: HANDLE,
        base_address: u64,
    ) -> Result<Option<ModuleLoadInfo>> {
        let mut module = Module::new(module_handle, base_address)?;

        trace!(
            "Loading module {} at {:x}",
            module.name().display(),
            base_address
        );

        if module.machine() == Machine::X64 && process::is_wow64_process(self.process_handle) {
            // We ignore native dlls in wow64 processes.
            return Ok(None);
        }

        if self.sym_initialize_state == SymInitalizeState::Initialized {
            if let Err(e) = module.sym_load_module(self.process_handle) {
                error!("Error loading symbols: {:?}", e);
            }
        }

        let module_load_info = ModuleLoadInfo::new(module.path(), base_address);
        let base_address = module.base_address();
        if let Some(old_value) = self.modules.insert(base_address, module) {
            error!(
                "Existing module {} replace at base_address {}",
                old_value.path().display(),
                base_address
            );
        }

        Ok(Some(module_load_info))
    }

    pub fn unload_module(&mut self, base_address: u64) {
        self.modules.remove(&base_address);
    }

    pub fn prepare_to_resume(&mut self) -> Result<()> {
        // When resuming, we take extra care to avoid missing breakpoints. There are 2 race
        // conditions considered here:
        //
        // * Threads A and B hit a breakpoint at literally the same time, so we have
        //   multiple events already queued to handle those breakpoints.
        //   Restoring the breakpoint for thread A should not interfere with restoring
        //   the original code for thread B.
        // * Thread A hit a breakpoint, thread B is 1 instruction away from hitting
        //   that breakpoint. Thread B cannot miss the breakpoint when thread A is resumed.
        //
        // To avoid these possible races, when resuming, we only let a single thread go **if**
        // we're single stepping any threads.

        if self.single_step.is_empty() {
            // Resume all threads if we aren't waiting for any threads to single step.
            for thread_info in self.thread_info.values_mut() {
                thread_info.resume_thread()?;
            }
        } else {
            // We will single step a single thread, but we must first make sure all
            // threads are suspended.
            for thread_info in self.thread_info.values_mut() {
                thread_info.suspend_thread()?;
            }

            // Now pick a random thread to resume.
            let idx = thread_rng().gen_range(0..self.single_step.len());
            let (handle, step_state) = self.single_step.iter().nth(idx).unwrap();
            let thread_info = self.thread_info.get_mut(handle).unwrap();

            thread_info.resume_thread()?;

            // We may also need to remove a breakpoint.
            if let StepState::RestoreBreakpointAfterStep { pc } = step_state {
                // We are stepping to remove the breakpoint. After we've stepped,
                // we must restore the breakpoint (which is done on the subsequent
                // call to this function).
                self.restore_breakpoint_pc = Some(*pc);
            }
        }

        // The current context will not be valid after resuming.
        self.current_context = None;

        Ok(())
    }

    pub fn prepare_to_step(&mut self) -> Result<bool> {
        // Don't change the reason we're single stepping on this thread if
        // we previously set the reason (e.g. so we would restore a breakpoint).
        self.single_step
            .entry(self.current_thread_id)
            .or_insert(StepState::SingleStep);

        let current_thread_handle = self.current_thread_handle; //borrowck
        let context = self.get_current_context_mut()?;
        context.set_single_step(true);
        context.set_thread_context(current_thread_handle)?;

        Ok(true)
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
        Ok(self.current_context.as_mut().unwrap())
    }

    pub fn read_register_u64(&mut self, reg: iced_x86::Register) -> Result<u64> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_register_u64(reg))
    }

    pub fn read_program_counter(&mut self) -> Result<u64> {
        self.read_register_u64(iced_x86::Register::RIP)
    }

    pub fn read_flags_register(&mut self) -> Result<u32> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_flags())
    }

    pub fn read_memory(&mut self, remote_address: LPCVOID, buf: &mut [impl Copy]) -> Result<()> {
        process::read_memory_array(self.process_handle, remote_address, buf)?;

        // We don't remove breakpoints when processing an event, so it's possible that the
        // memory we read contains **our** breakpoints instead of the original code.
        let remote_address = remote_address as u64;
        let module = self.module_from_address(remote_address);
        let range = remote_address..(remote_address + buf.len() as u64);

        let u8_buf = unsafe {
            std::slice::from_raw_parts_mut(buf.as_mut_ptr() as *mut u8, std::mem::size_of_val(buf))
        };
        for (address, breakpoint) in module.breakpoints_for_range(range) {
            if let Some(original_byte) = breakpoint.get_original_byte() {
                let idx = *address - remote_address;
                u8_buf[idx as usize] = original_byte;
            }
        }

        Ok(())
    }

    /// Handle a breakpoint that we set (as opposed to a breakpoint in user code, e.g.
    /// assertion.)
    ///
    /// Return the breakpoint id if it should be reported to the client.
    pub fn handle_breakpoint(&mut self, pc: u64) -> Result<Option<BreakpointId>> {
        let process_handle = self.process_handle; // borrowck
        let id;
        let mut renable_after_step = true;
        {
            let bp = self
                .get_breakpoint_for_address(pc)
                .expect("debugger should have checked already");
            id = bp.id();

            bp.increment_hit_count();
            bp.disable(process_handle)?;

            if let BreakpointType::OneTime = bp.kind() {
                renable_after_step = false;
            }
        }

        let current_thread_handle = self.current_thread_handle;

        // We need to execute the instruction we overwrote with our breakpoint, so move the
        // instruction pointer back, and if we are going to restore the breakpoint, set
        // single stepping.
        let context = self.get_current_context_mut()?;
        context.set_program_counter(pc);

        if renable_after_step {
            context.set_single_step(true);
        }

        context.set_thread_context(current_thread_handle)?;

        if renable_after_step {
            // Remember that on the current thread, we need to restore the original byte.
            // When resuming, if we pick the current thread, we'll remove the breakpoint.
            self.single_step.insert(
                self.current_thread_id,
                StepState::RestoreBreakpointAfterStep { pc },
            );
        }

        Ok(Some(id))
    }

    pub fn set_exited(&mut self) -> Result<()> {
        self.exited = true;

        if self.sym_initialize_state == SymInitalizeState::Initialized {
            let dbghelp = dbghelp::lock()?;
            dbghelp.sym_cleanup(self.process_handle)?;
        }
        Ok(())
    }
}
