// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::collapsible_if)]
#![allow(clippy::needless_return)]
#![allow(clippy::unreadable_literal)]
#![allow(clippy::single_match)]
#![allow(clippy::redundant_closure)]
#![allow(clippy::redundant_clone)]
use std::{
    collections::{hash_map, HashMap},
    ffi::OsString,
    fs,
    mem::MaybeUninit,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::{Child, Command},
};

use anyhow::{Context, Result};
use log::{error, trace};
use win_util::{check_winapi, file, last_os_error, process};
use winapi::{
    shared::{
        minwindef::{DWORD, FALSE, LPCVOID, LPVOID, TRUE},
        winerror::ERROR_SEM_TIMEOUT,
    },
    um::{
        dbghelp::ADDRESS64,
        debugapi::{ContinueDebugEvent, WaitForDebugEvent},
        errhandlingapi::GetLastError,
        handleapi::CloseHandle,
        minwinbase::{
            CREATE_PROCESS_DEBUG_INFO, CREATE_THREAD_DEBUG_INFO, EXCEPTION_BREAKPOINT,
            EXCEPTION_DEBUG_INFO, EXCEPTION_SINGLE_STEP, EXIT_PROCESS_DEBUG_INFO,
            EXIT_THREAD_DEBUG_INFO, LOAD_DLL_DEBUG_INFO, RIP_INFO, UNLOAD_DLL_DEBUG_INFO,
        },
        winbase::{DebugSetProcessKillOnExit, DEBUG_ONLY_THIS_PROCESS, INFINITE},
        winnt::{
            DBG_CONTINUE, DBG_EXCEPTION_NOT_HANDLED, HANDLE, IMAGE_FILE_MACHINE_AMD64,
            IMAGE_FILE_MACHINE_I386,
        },
    },
};

use crate::{
    dbghelp::{self, FrameContext, SymInfo},
    debug_event::{DebugEvent, DebugEventInfo},
    stack,
};

// When debugging a WoW64 process, we see STATUS_WX86_BREAKPOINT in addition to EXCEPTION_BREAKPOINT
const STATUS_WX86_BREAKPOINT: u32 = ::winapi::shared::ntstatus::STATUS_WX86_BREAKPOINT as u32;

/// Uniquely identify a breakpoint.
#[derive(Copy, Clone, Debug, Eq, Hash, PartialEq)]
pub struct BreakpointId(pub u32);

#[derive(Copy, Clone)]
enum StepState {
    Breakpoint { pc: u64 },
    SingleStep,
}

#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub enum BreakpointType {
    Counter,
    OneTime,
    StepOut { rsp: u64 },
}

struct ModuleBreakpoint {
    rva: u64,
    kind: BreakpointType,
    id: BreakpointId,
}

#[allow(unused)]
struct UnresolvedBreakpoint {
    sym: String,
    kind: BreakpointType,
    id: BreakpointId,
}

/// A breakpoint for a specific target. We can say it is bound because we know exactly
/// where to set it, but it might disabled.
#[derive(Clone)]
struct Breakpoint {
    /// The address of the breakpoint.
    ip: u64,

    kind: BreakpointType,

    /// Currently active?
    enabled: bool,

    /// Holds the original byte at the location.
    original_byte: Option<u8>,

    hit_count: usize,

    id: BreakpointId,
}

pub struct StackFrame {
    return_address: u64,
    stack_pointer: u64,
}

impl StackFrame {
    pub fn new(return_address: u64, stack_pointer: u64) -> Self {
        StackFrame {
            return_address,
            stack_pointer,
        }
    }

    pub fn return_address(&self) -> u64 {
        self.return_address
    }

    pub fn stack_pointer(&self) -> u64 {
        self.stack_pointer
    }
}

struct Module {
    path: PathBuf,
    file_handle: HANDLE,
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
            file_handle: module_handle,
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
                self.file_handle,
                &self.path,
                self.base_address,
                self.image_size,
            )?;

            self.sym_module_loaded = true;
        }

        Ok(())
    }

    fn name(&self) -> &Path {
        // Unwrap guaranteed by construction, we always have a filename.
        self.path.file_stem().unwrap().as_ref()
    }
}

impl Drop for Module {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.file_handle) };
    }
}

pub struct Target {
    process_id: DWORD,
    process_handle: HANDLE,
    current_thread_handle: HANDLE,
    saw_initial_bp: bool,
    saw_initial_wow64_bp: bool,

    // Track if we need to call SymInitialize for the process and if we need to notify
    // dbghelp about loaded/unloaded dlls.
    sym_initialized: bool,
    exited: bool,

    thread_handles: fnv::FnvHashMap<DWORD, HANDLE>,

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
    fn new(
        process_id: DWORD,
        thread_id: DWORD,
        process_handle: HANDLE,
        thread_handle: HANDLE,
    ) -> Self {
        let mut thread_handles = fnv::FnvHashMap::default();
        thread_handles.insert(thread_id, thread_handle);

        Self {
            process_id,
            current_thread_handle: thread_handle,
            process_handle,
            saw_initial_bp: false,
            saw_initial_wow64_bp: false,
            sym_initialized: false,
            exited: false,
            thread_handles,
            current_context: None,
            context_is_modified: false,
            modules: fnv::FnvHashMap::default(),
            breakpoints: fnv::FnvHashMap::default(),
            single_step: fnv::FnvHashMap::default(),
        }
    }

    fn modules(&self) -> hash_map::Iter<u64, Module> {
        self.modules.iter()
    }

    fn initial_bp(&mut self, load_symbols: bool) -> Result<()> {
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

    fn sym_initialize(&mut self) -> Result<()> {
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

    /// Register the module loaded at `base_address`, returning the module name.
    fn load_module(&mut self, module_handle: HANDLE, base_address: u64) -> Result<Option<PathBuf>> {
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

        let module_name = module.name().to_owned();
        if self.sym_initialized {
            if let Err(e) = module.sym_load_module(self.process_handle) {
                error!("Error loading symbols: {}", e);
            }
        }

        let base_address = module.base_address;
        if let Some(old_value) = self.modules.insert(base_address, module) {
            error!(
                "Existing module {} replace at base_address {}",
                old_value.path.display(),
                base_address
            );
        }

        Ok(Some(module_name))
    }

    fn unload_module(&mut self, base_address: u64) {
        // Drop the module and remove any breakpoints.
        if let Some(module) = self.modules.remove(&base_address) {
            let image_size = module.image_size as u64;
            self.breakpoints
                .retain(|&ip, _| ip < base_address || ip >= base_address + image_size);
        }
    }

    fn apply_absolute_breakpoint(
        &mut self,
        address: u64,
        kind: BreakpointType,
        id: BreakpointId,
    ) -> Result<()> {
        let original_byte: u8 = process::read_memory(self.process_handle, address as LPVOID)?;

        self.breakpoints
            .entry(address)
            .and_modify(|e| {
                e.kind = kind;
                e.enabled = true;
                e.original_byte = Some(original_byte);
                e.id = id;
            })
            .or_insert(Breakpoint {
                ip: address,
                kind,
                enabled: true,
                original_byte: Some(original_byte),
                hit_count: 0,
                id,
            });

        write_instruction_byte(self.process_handle, address, 0xcc)?;

        Ok(())
    }

    fn apply_module_breakpoints(
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
                (acc.0.min(bp.rva), acc.1.max(bp.rva))
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
            let ip = base_address + mbp.rva;
            let offset = (mbp.rva - min) as usize;

            trace!("Setting breakpoint at {:x}", ip);

            let bp = Breakpoint {
                ip,
                kind: mbp.kind,
                enabled: true,
                original_byte: Some(buffer[offset]),
                hit_count: 0,
                id: mbp.id,
            };

            buffer[offset] = 0xcc;

            self.breakpoints.insert(ip, bp);
        }

        process::write_memory_slice(self.process_handle, remote_address, &buffer[..])?;
        process::flush_instruction_cache(self.process_handle, remote_address, region_size)?;

        Ok(())
    }

    fn prepare_to_resume(&mut self) -> Result<()> {
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

    fn read_register_u64(&mut self, reg: iced_x86::Register) -> Result<u64> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_register_u64(reg))
    }

    fn read_flags_register(&mut self) -> Result<u32> {
        let current_context = self.get_current_context()?;
        Ok(current_context.get_flags())
    }

    /// Handle a breakpoint that we set (as opposed to a breakpoint in user code, e.g.
    /// assertion.)
    ///
    /// Return the breakpoint id if it should be reported to the client.
    fn handle_breakpoint(&mut self, pc: u64) -> Result<Option<BreakpointId>> {
        enum HandleBreakpoint {
            User(BreakpointId, bool),
            StepOut(u64),
        }

        let handle_breakpoint = {
            let bp = self.breakpoints.get_mut(&pc).unwrap();

            bp.hit_count += 1;

            write_instruction_byte(self.process_handle, bp.ip, bp.original_byte.unwrap())?;

            match bp.kind {
                BreakpointType::OneTime => {
                    bp.enabled = false;
                    bp.original_byte = None;

                    // We are clearing the breakpoint after hitting it, so we do not need
                    // to single step.
                    HandleBreakpoint::User(bp.id, false)
                }

                BreakpointType::Counter => {
                    // Single step so we can restore the breakpoint after stepping.
                    HandleBreakpoint::User(bp.id, true)
                }

                BreakpointType::StepOut { rsp } => HandleBreakpoint::StepOut(rsp),
            }
        };

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

        Ok(match handle_breakpoint {
            HandleBreakpoint::User(id, _) => Some(id),
            HandleBreakpoint::StepOut(_) => None,
        })
    }

    fn handle_single_step(&mut self, step_state: StepState) -> Result<()> {
        match step_state {
            StepState::Breakpoint { pc } => {
                write_instruction_byte(self.process_handle, pc, 0xcc)?;
            }
            _ => {}
        }

        self.single_step.remove(&self.current_thread_handle);
        Ok(())
    }

    fn prepare_to_step(&mut self) -> Result<bool> {
        // Don't change the reason we're single stepping on this thread if
        // we previously set the reason (e.g. so we would restore a breakpoint).
        self.single_step
            .entry(self.current_thread_handle)
            .or_insert(StepState::SingleStep);

        let context = self.get_current_context_mut()?;
        context.set_single_step(true);

        Ok(true)
    }

    fn set_exited(&mut self) -> Result<()> {
        self.exited = true;

        if self.sym_initialized {
            let dbghelp = dbghelp::lock()?;
            dbghelp.sym_cleanup(self.process_handle)?;
        }
        Ok(())
    }
}

fn write_instruction_byte(process_handle: HANDLE, ip: u64, b: u8) -> Result<()> {
    let orig_byte = [b; 1];
    let remote_address = ip as LPVOID;
    process::write_memory_slice(process_handle, remote_address, &orig_byte)?;
    process::flush_instruction_cache(process_handle, remote_address, orig_byte.len())?;
    Ok(())
}

#[rustfmt::skip]
#[allow(clippy::trivially_copy_pass_by_ref)]
pub trait DebugEventHandler {
    fn on_exception(&mut self, _debugger: &mut Debugger, _info: &EXCEPTION_DEBUG_INFO, _process_handle: HANDLE) -> DWORD {
        // Continue normal exception handling processing
        DBG_EXCEPTION_NOT_HANDLED
    }
    fn on_create_thread(&mut self, _debugger: &mut Debugger, _info: &CREATE_THREAD_DEBUG_INFO) {}
    fn on_create_process(&mut self, _debugger: &mut Debugger, _info: &CREATE_PROCESS_DEBUG_INFO) {}
    fn on_exit_thread(&mut self, _debugger: &mut Debugger, _info: &EXIT_THREAD_DEBUG_INFO) {}
    fn on_exit_process(&mut self, _debugger: &mut Debugger, _info: &EXIT_PROCESS_DEBUG_INFO) {}
    fn on_load_dll(&mut self, _debugger: &mut Debugger, _info: &LOAD_DLL_DEBUG_INFO) {}
    fn on_unload_dll(&mut self, _debugger: &mut Debugger, _info: &UNLOAD_DLL_DEBUG_INFO) {}
    fn on_output_debug_string(&mut self, _debugger: &mut Debugger, _message: String) {}
    fn on_output_debug_os_string(&mut self, _debugger: &mut Debugger, _message: OsString) {}
    fn on_rip(&mut self, _debugger: &mut Debugger, _info: &RIP_INFO) {}
    fn on_poll(&mut self, _debugger: &mut Debugger) {}
    fn on_breakpoint(&mut self, _debugger: &mut Debugger, _id: BreakpointId) {}
}

#[derive(Default)]
struct ContinueDebugEventArguments {
    process_id: u32,
    thread_id: u32,
    continue_status: u32,
}

pub struct Debugger {
    target: Target,
    continue_args: Option<ContinueDebugEventArguments>,
    registered_breakpoints: HashMap<PathBuf, Vec<ModuleBreakpoint>>,
    symbolic_breakpoints: HashMap<PathBuf, Vec<UnresolvedBreakpoint>>,
    breakpoint_count: u32,
}

impl Debugger {
    pub fn init(
        mut command: Command,
        callbacks: &mut impl DebugEventHandler,
    ) -> Result<(Self, Child)> {
        let child = command.creation_flags(DEBUG_ONLY_THIS_PROCESS).spawn()?;

        check_winapi(|| unsafe { DebugSetProcessKillOnExit(TRUE) })
            .context("Setting DebugSetProcessKillOnExit to TRUE")?;

        // Call once to get our initial CreateProcess event.
        //
        // The kernel blocks the process from starting until the CreateProcess event is processed,
        // so we must wait forever. Otherwise, we would exit the debugger and subsequently wait
        // for our target to exit.
        //
        // The kernel seems to reliably generate the CreateProcess event, so we should never
        // have a true hang here.
        let mut de = MaybeUninit::uninit();
        if unsafe { WaitForDebugEvent(de.as_mut_ptr(), INFINITE) } == FALSE {
            return Err(last_os_error());
        }

        let de = unsafe { de.assume_init() };
        let de = DebugEvent::new(&de);
        if let DebugEventInfo::CreateProcess(info) = de.info() {
            trace!("DebugEvent: {}", de);

            let mut target =
                Target::new(de.process_id(), de.thread_id(), info.hProcess, info.hThread);

            let base_address = info.lpBaseOfImage as u64;
            if let Err(e) = target.load_module(info.hFile, base_address) {
                error!("Error loading process module: {}", e);
            }

            let mut debugger = Debugger {
                target,
                continue_args: None,
                registered_breakpoints: HashMap::default(),
                symbolic_breakpoints: HashMap::default(),
                breakpoint_count: 0,
            };
            callbacks.on_create_process(&mut debugger, *info);

            if unsafe { ContinueDebugEvent(de.process_id(), de.thread_id(), DBG_CONTINUE) } == FALSE
            {
                return Err(last_os_error());
            }

            Ok((debugger, child))
        } else {
            anyhow::bail!("Unexpected event: {}", de)
        }
    }

    fn next_breakpoint_id(&mut self) -> BreakpointId {
        let id = BreakpointId(self.breakpoint_count);
        self.breakpoint_count += 1;
        id
    }

    pub fn register_symbolic_breakpoint(
        &mut self,
        sym: &str,
        kind: BreakpointType,
    ) -> Result<BreakpointId> {
        let (module, func) = if let Some(split_at) = sym.find('!') {
            (&sym[..split_at], &sym[split_at + 1..])
        } else {
            ("*", sym)
        };

        let id = self.next_breakpoint_id();

        let values = self
            .symbolic_breakpoints
            .entry(module.into())
            .or_insert_with(|| Vec::new());

        values.push(UnresolvedBreakpoint {
            kind,
            id,
            sym: func.into(),
        });

        Ok(id)
    }

    pub fn register_breakpoint(
        &mut self,
        module: &Path,
        rva: u64,
        kind: BreakpointType,
    ) -> BreakpointId {
        let id = self.next_breakpoint_id();

        let module_breakpoints = self
            .registered_breakpoints
            .entry(module.into())
            .or_insert_with(|| vec![]);

        module_breakpoints.push(ModuleBreakpoint { rva, kind, id });
        id
    }

    pub fn register_absolute_breakpoint(
        &mut self,
        address: u64,
        kind: BreakpointType,
    ) -> Result<BreakpointId> {
        let id = self.next_breakpoint_id();

        self.target.apply_absolute_breakpoint(address, kind, id)?;
        // TODO: find module the address belongs to and add to registered_breakpoints

        Ok(id)
    }

    /// Return true if an event was process, false if timing out, or an error.
    pub fn process_event(
        &mut self,
        callbacks: &mut impl DebugEventHandler,
        timeout_ms: DWORD,
    ) -> Result<()> {
        let mut de = MaybeUninit::uninit();
        if unsafe { WaitForDebugEvent(de.as_mut_ptr(), timeout_ms) } == TRUE {
            let de = unsafe { de.assume_init() };
            let de = DebugEvent::new(&de);
            trace!("DebugEvent: {}", de);

            let continue_status = self.dispatch_event(&de, callbacks);
            self.continue_args = Some(ContinueDebugEventArguments {
                continue_status,
                process_id: de.process_id(),
                thread_id: de.thread_id(),
            });
        } else {
            self.continue_args = None;

            let err = unsafe { GetLastError() };
            if err != ERROR_SEM_TIMEOUT {
                return Err(last_os_error());
            }

            trace!("timeout waiting for debug event");
        }

        Ok(())
    }

    pub fn continue_debugging(&mut self) -> Result<()> {
        if let Some(continue_args) = self.continue_args.take() {
            self.target.prepare_to_resume()?;

            if unsafe {
                ContinueDebugEvent(
                    continue_args.process_id,
                    continue_args.thread_id,
                    continue_args.continue_status,
                )
            } == FALSE
            {
                return Err(last_os_error());
            }
        }

        Ok(())
    }

    pub fn run(&mut self, callbacks: &mut impl DebugEventHandler) -> Result<()> {
        while !self.target.exited {
            // Poll between every event so a client can add generic logic instead needing to
            // handle every possible event.
            callbacks.on_poll(self);

            // Timeout of 1 second is somewhat arbitrary.
            // We should avoid too much time polling (e.g. reading the target stdout/stderr) but
            // at the same time be granular enough to detect a hung process in a reasonable time.
            self.process_event(callbacks, 1000)?;
            self.continue_debugging()?;
        }

        Ok(())
    }

    pub fn quit_debugging(&self) {
        if !self.target.exited {
            trace!("timeout - terminating pid: {}", self.target.process_id);
            process::terminate(self.target.process_handle);
        }
    }

    fn dispatch_event(&mut self, de: &DebugEvent, callbacks: &mut impl DebugEventHandler) -> u32 {
        let mut continue_status = DBG_CONTINUE;

        if let DebugEventInfo::CreateThread(info) = de.info() {
            self.target.current_thread_handle = info.hThread;
            self.target
                .thread_handles
                .insert(de.thread_id(), info.hThread);
        } else {
            self.target.current_thread_handle =
                *self.target.thread_handles.get(&de.thread_id()).unwrap();
        }

        match de.info() {
            DebugEventInfo::CreateProcess(_info) => {
                // We pass DEBUG_ONLY_THIS_PROCESS when spawning
                // and will only see the 1 CreateProcess event handled previously.
                unreachable!("Nested targets not supported");
            }

            DebugEventInfo::LoadDll(info) => {
                let base_address = info.lpBaseOfDll as u64;
                match self.target.load_module(info.hFile, base_address) {
                    Ok(Some(module_name)) => {
                        // We must defer adding any breakpoints until we've seen the initial
                        // breakpoint notification from the OS. Otherwise we may set
                        // breakpoints in startup code before the debugger is properly
                        // initialized.
                        if self.target.saw_initial_bp {
                            self.apply_module_breakpoints(module_name, base_address)
                        }
                    }
                    Ok(None) => {}
                    Err(e) => {
                        error!("Error loading module: {}", e);
                    }
                }

                callbacks.on_load_dll(self, *info);
            }

            DebugEventInfo::UnloadDll(info) => {
                self.target.unload_module(info.lpBaseOfDll as u64);

                callbacks.on_unload_dll(self, *info);
            }

            DebugEventInfo::Exception(info) => {
                continue_status = match self.dispatch_exception_event(*info, callbacks) {
                    Ok(status) => status,
                    Err(e) => {
                        error!("Error processing exception: {}", e);
                        DBG_EXCEPTION_NOT_HANDLED
                    }
                }
            }

            DebugEventInfo::ExitProcess(info) => {
                if let Err(err) = self.target.set_exited() {
                    error!("Error cleaning up after process exit: {}", err);
                }
                callbacks.on_exit_process(self, *info);
            }

            DebugEventInfo::CreateThread(info) => {
                callbacks.on_create_thread(self, *info);
            }

            DebugEventInfo::ExitThread(info) => {
                callbacks.on_exit_thread(self, *info);
                self.target.thread_handles.remove(&de.thread_id());
            }

            DebugEventInfo::OutputDebugString(info) => {
                // Remove the terminating NUL as it's not needed in a Rust string.
                let length = info.nDebugStringLength.saturating_sub(1) as usize;
                if info.fUnicode != 0 {
                    if let Ok(message) = process::read_wide_string(
                        self.target.process_handle,
                        info.lpDebugStringData as LPCVOID,
                        length,
                    ) {
                        callbacks.on_output_debug_os_string(self, message);
                    }
                } else {
                    if let Ok(message) = process::read_narrow_string(
                        self.target.process_handle,
                        info.lpDebugStringData as LPCVOID,
                        length,
                    ) {
                        callbacks.on_output_debug_string(self, message);
                    }
                }
            }

            DebugEventInfo::Rip(info) => {
                callbacks.on_rip(self, *info);
            }

            DebugEventInfo::Unknown => {}
        }

        continue_status
    }

    fn dispatch_exception_event(
        &mut self,
        info: &EXCEPTION_DEBUG_INFO,
        callbacks: &mut impl DebugEventHandler,
    ) -> Result<u32> {
        match is_debugger_notification(
            info.ExceptionRecord.ExceptionCode,
            info.ExceptionRecord.ExceptionAddress as u64,
            &self.target,
        ) {
            Some(DebuggerNotification::InitialBreak) => {
                let modules = {
                    self.target.saw_initial_bp = true;
                    let load_symbols = !self.symbolic_breakpoints.is_empty();
                    self.target.initial_bp(load_symbols)?;
                    self.target
                        .modules()
                        .map(|(addr, module)| (*addr, (*module).name().to_owned()))
                        .collect::<Vec<(u64, PathBuf)>>()
                };
                for (base_address, module) in modules {
                    self.apply_module_breakpoints(module, base_address)
                }
                Ok(DBG_CONTINUE)
            }
            Some(DebuggerNotification::InitialWow64Break) => {
                self.target.saw_initial_wow64_bp = true;
                Ok(DBG_CONTINUE)
            }
            Some(DebuggerNotification::Clr) => Ok(DBG_CONTINUE),
            Some(DebuggerNotification::Breakpoint(pc)) => {
                if let Some(bp_id) = self.target.handle_breakpoint(pc)? {
                    callbacks.on_breakpoint(self, bp_id);
                }
                Ok(DBG_CONTINUE)
            }
            Some(DebuggerNotification::SingleStep(step_state)) => {
                self.target.handle_single_step(step_state)?;
                Ok(DBG_CONTINUE)
            }
            None => {
                let process_handle = self.target.process_handle;
                Ok(callbacks.on_exception(self, info, process_handle))
            }
        }
    }

    fn apply_module_breakpoints(&mut self, module_name: impl AsRef<Path>, base_address: u64) {
        // We remove because we only need to resolve the RVA once even if the dll is loaded
        // multiple times (e.g. in the same process via LoadLibrary/FreeLibrary) or if the
        // same dll is loaded in different processes.
        if let Some(unresolved_breakpoints) = self.symbolic_breakpoints.remove(module_name.as_ref())
        {
            let cloned_module_name = PathBuf::from(module_name.as_ref()).to_owned();
            let rva_breakpoints = self
                .registered_breakpoints
                .entry(cloned_module_name)
                .or_insert_with(|| Vec::new());

            match dbghelp::lock() {
                Ok(dbghelp) => {
                    for bp in unresolved_breakpoints {
                        match dbghelp.sym_from_name(
                            self.target.process_handle,
                            module_name.as_ref(),
                            &bp.sym,
                        ) {
                            Ok(sym) => {
                                rva_breakpoints.push(ModuleBreakpoint {
                                    rva: sym.address() - base_address,
                                    kind: bp.kind,
                                    id: bp.id,
                                });
                            }
                            Err(e) => {
                                error!("Can't set symbolic breakpoints: {}", e);
                            }
                        }
                    }
                }
                Err(e) => {
                    error!("Can't set symbolic breakpoints: {}", e);
                }
            }
        }

        if let Some(breakpoints) = self.registered_breakpoints.get(module_name.as_ref()) {
            if let Err(e) = self
                .target
                .apply_module_breakpoints(base_address, breakpoints)
            {
                error!("Error applying breakpoints: {}", e);
            }
        }
    }

    pub fn get_current_stack(&mut self) -> Result<stack::DebugStack> {
        // If we fail to initialize symbols, we'll skip collecting symbols
        // when walking the stack. Note that if we see multiple exceptions
        // in the same process, we will retry initializing symbols.
        // We could retry in a loop (apparently it can fail but later
        // succeed), but symbols aren't strictly necessary, so we won't
        // be too aggressive in dealing with failures.
        let resolve_symbols = self.target.sym_initialize().is_ok();
        return stack::get_stack(
            self.target.process_handle,
            self.target.current_thread_handle,
            resolve_symbols,
        );
    }

    pub fn get_symbol(&self, pc: u64) -> Result<SymInfo> {
        let dbghelp = dbghelp::lock()?;
        dbghelp.sym_from_inline_context(self.target.process_handle, pc, 0)
    }

    pub fn get_current_thread_id(&self) -> u64 {
        self.target.current_thread_handle as u64
    }

    pub fn read_register_u64(&mut self, reg: iced_x86::Register) -> Result<u64> {
        self.target.read_register_u64(reg)
    }

    pub fn read_flags_register(&mut self) -> Result<u32> {
        self.target.read_flags_register()
    }

    pub fn get_current_target_memory<T: Copy>(
        &self,
        remote_address: LPCVOID,
        buf: &mut [T],
    ) -> Result<()> {
        process::read_memory_array(self.target.process_handle, remote_address, buf)?;
        Ok(())
    }

    pub fn get_current_frame(&self) -> Result<StackFrame> {
        let dbghlp = dbghelp::lock()?;

        let mut return_address = ADDRESS64::default();
        let mut stack_pointer = ADDRESS64::default();
        dbghlp.stackwalk_ex(
            self.target.process_handle,
            self.target.current_thread_handle,
            |_frame_context, frame| {
                return_address = frame.AddrReturn;
                stack_pointer = frame.AddrStack;

                // We only want the top frame, so stop walking.
                false
            },
        )?;

        Ok(StackFrame::new(return_address.Offset, stack_pointer.Offset))
    }

    pub fn step(&mut self) -> Result<bool> {
        self.target.prepare_to_step()?;
        self.continue_debugging()?;
        Ok(true)
    }

    /// Find the return address (and stack pointer to cover recursion), set a breakpoint
    /// (internal, not reported to the client) that gets cleared after stepping out.
    ///
    /// The return address and stack pointer are returned so the caller can check
    /// that the step out is complete regardless of other debug events that may happen before
    /// hitting the breakpoint at the return address.
    pub fn prepare_to_step_out(&mut self) -> Result<StackFrame> {
        let stack_frame = self.get_current_frame()?;
        let address = stack_frame.return_address();
        self.register_absolute_breakpoint(
            address,
            BreakpointType::StepOut {
                rsp: stack_frame.stack_pointer(),
            },
        )?;

        Ok(stack_frame)
    }
}

enum DebuggerNotification {
    Clr,
    InitialBreak,
    InitialWow64Break,
    Breakpoint(u64),
    SingleStep(StepState),
}

fn is_debugger_notification(
    exception_code: u32,
    exception_address: u64,
    target: &Target,
) -> Option<DebuggerNotification> {
    // The CLR debugger notification exception is not a crash:
    //   https://github.com/dotnet/coreclr/blob/9ee6b8a33741cc5f3eb82e990646dd3a81de996a/src/debug/inc/dbgipcevents.h#L37
    const CLRDBG_NOTIFICATION_EXCEPTION_CODE: DWORD = 0x04242420;

    match exception_code {
        // Not a breakpoint, but sent to debuggers as a notification that the process has
        // managed code.
        CLRDBG_NOTIFICATION_EXCEPTION_CODE => Some(DebuggerNotification::Clr),

        // The first EXCEPTION_BREAKPOINT is sent to debuggers like us.
        EXCEPTION_BREAKPOINT => {
            if target.saw_initial_bp {
                if target.breakpoints.contains_key(&exception_address) {
                    Some(DebuggerNotification::Breakpoint(exception_address))
                } else {
                    None
                }
            } else {
                Some(DebuggerNotification::InitialBreak)
            }
        }

        // We may see a second breakpoint (STATUS_WX86_BREAKPOINT) when debugging a
        // WoW64 process, this is also a debugger notification, not a real breakpoint.
        STATUS_WX86_BREAKPOINT => {
            if target.saw_initial_wow64_bp {
                None
            } else {
                Some(DebuggerNotification::InitialWow64Break)
            }
        }

        EXCEPTION_SINGLE_STEP => {
            if let Some(&step_state) = target.single_step.get(&target.current_thread_handle) {
                Some(DebuggerNotification::SingleStep(step_state))
            } else {
                // Unexpected single step - could be a logic bug in the debugger or less
                // likely but possibly an intentional exception in the debug target.
                // Without a good way to know the difference, always report the exception
                // to any callbacks.
                None
            }
        }

        _ => None,
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
