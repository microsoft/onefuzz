// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::collapsible_if)]
#![allow(clippy::needless_return)]
#![allow(clippy::unreadable_literal)]
#![allow(clippy::single_match)]
#![allow(clippy::redundant_closure)]
#![allow(clippy::redundant_clone)]
use std::{
    collections::HashMap,
    ffi::OsString,
    mem::MaybeUninit,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::{Child, Command},
};

use anyhow::{Context, Result};
use log::{debug, error, trace};
use win_util::{check_winapi, last_os_error, process};
use winapi::{
    shared::{
        minwindef::{DWORD, FALSE, LPCVOID, TRUE},
        winerror::ERROR_SEM_TIMEOUT,
    },
    um::{
        dbghelp::ADDRESS64,
        debugapi::{ContinueDebugEvent, WaitForDebugEvent},
        errhandlingapi::GetLastError,
        minwinbase::{EXCEPTION_BREAKPOINT, EXCEPTION_DEBUG_INFO, EXCEPTION_SINGLE_STEP},
        winbase::{DebugSetProcessKillOnExit, DEBUG_ONLY_THIS_PROCESS, INFINITE},
        winnt::{DBG_CONTINUE, DBG_EXCEPTION_NOT_HANDLED, HANDLE},
    },
};

use crate::target::{Module, Target};
use crate::{
    dbghelp::{self, ModuleInfo, SymInfo, SymLineInfo},
    debug_event::{DebugEvent, DebugEventInfo},
    stack,
};

// When debugging a WoW64 process, we see STATUS_WX86_BREAKPOINT in addition to EXCEPTION_BREAKPOINT
const STATUS_WX86_BREAKPOINT: u32 = ::winapi::shared::ntstatus::STATUS_WX86_BREAKPOINT as u32;

/// Uniquely identify a breakpoint.
#[derive(Copy, Clone, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct BreakpointId(pub u32);

#[derive(Copy, Clone)]
pub(crate) enum StepState {
    Breakpoint { pc: u64 },
    SingleStep,
}

#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub enum BreakpointType {
    Counter,
    OneTime,
    StepOut { rsp: u64 },
}

pub(crate) struct ModuleBreakpoint {
    rva: u64,
    kind: BreakpointType,
    id: BreakpointId,
}

impl ModuleBreakpoint {
    pub fn rva(&self) -> u64 {
        self.rva
    }

    pub fn kind(&self) -> BreakpointType {
        self.kind
    }

    pub fn id(&self) -> BreakpointId {
        self.id
    }
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
pub struct Breakpoint {
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

impl Breakpoint {
    pub fn new(
        ip: u64,
        kind: BreakpointType,
        enabled: bool,
        original_byte: Option<u8>,
        hit_count: usize,
        id: BreakpointId,
    ) -> Self {
        Breakpoint {
            ip,
            kind,
            enabled,
            original_byte,
            hit_count,
            id,
        }
    }

    pub fn ip(&self) -> u64 {
        self.ip
    }

    pub fn kind(&self) -> BreakpointType {
        self.kind
    }

    pub(crate) fn set_kind(&mut self, kind: BreakpointType) {
        self.kind = kind;
    }

    pub fn enabled(&self) -> bool {
        self.enabled
    }

    pub fn set_enabled(&mut self, enabled: bool) {
        self.enabled = enabled;
    }

    pub fn original_byte(&self) -> Option<u8> {
        self.original_byte
    }

    pub(crate) fn set_original_byte(&mut self, original_byte: Option<u8>) {
        self.original_byte = original_byte;
    }

    pub fn hit_count(&self) -> usize {
        self.hit_count
    }

    pub(crate) fn increment_hit_count(&mut self) {
        self.hit_count += 1;
    }

    pub fn id(&self) -> BreakpointId {
        self.id
    }

    pub(crate) fn set_id(&mut self, id: BreakpointId) {
        self.id = id;
    }
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

#[rustfmt::skip]
#[allow(clippy::trivially_copy_pass_by_ref)]
pub trait DebugEventHandler {
    fn on_exception(&mut self, _debugger: &mut Debugger, _info: &EXCEPTION_DEBUG_INFO, _process_handle: HANDLE) -> DWORD {
        // Continue normal exception handling processing
        DBG_EXCEPTION_NOT_HANDLED
    }
    fn on_create_process(&mut self, _debugger: &mut Debugger, _module: &Module) {}
    fn on_create_thread(&mut self, _debugger: &mut Debugger) {}
    fn on_exit_process(&mut self, _debugger: &mut Debugger, _exit_code: u32) {}
    fn on_exit_thread(&mut self, _debugger: &mut Debugger, _exit_code: u32) {}
    fn on_load_dll(&mut self, _debugger: &mut Debugger, _module: &Module) {}
    fn on_unload_dll(&mut self, _debugger: &mut Debugger, _base_address: u64) {}
    fn on_output_debug_string(&mut self, _debugger: &mut Debugger, _message: String) {}
    fn on_output_debug_os_string(&mut self, _debugger: &mut Debugger, _message: OsString) {}
    fn on_rip(&mut self, _debugger: &mut Debugger, _error: u32, _type: u32) {}
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
            let module = target
                .load_module(info.hFile, base_address)
                .context("Loading process module")?
                .unwrap();

            let mut debugger = Debugger {
                target,
                continue_args: None,
                registered_breakpoints: HashMap::default(),
                symbolic_breakpoints: HashMap::default(),
                breakpoint_count: 0,
            };
            callbacks.on_create_process(&mut debugger, &module);

            if unsafe { ContinueDebugEvent(de.process_id(), de.thread_id(), DBG_CONTINUE) } == FALSE
            {
                return Err(last_os_error());
            }

            Ok((debugger, child))
        } else {
            anyhow::bail!("Unexpected event: {}", de)
        }
    }

    pub fn target(&mut self) -> &mut Target {
        &mut self.target
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
            anyhow::bail!("no module name specified for breakpoint {}", sym);
        };

        let id = self.next_breakpoint_id();

        if self.target.saw_initial_bp() {
            self.target
                .set_symbolic_breakpoint(module, func, kind, id)?;
        } else {
            // Defer setting the breakpoint until seeing the initial breakpoint.
            let values = self
                .symbolic_breakpoints
                .entry(module.into())
                .or_insert_with(|| Vec::new());

            values.push(UnresolvedBreakpoint {
                kind,
                id,
                sym: func.into(),
            });
        }

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
        while !self.target.exited() {
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
        if !self.target.exited() {
            trace!("timeout - terminating pid: {}", self.target.process_id());
            process::terminate(self.target.process_handle());
        }
    }

    fn dispatch_event(&mut self, de: &DebugEvent, callbacks: &mut impl DebugEventHandler) -> u32 {
        let mut continue_status = DBG_CONTINUE;

        if let DebugEventInfo::CreateThread(info) = de.info() {
            self.target.create_new_thread(info.hThread, de.thread_id());
        } else {
            self.target.set_current_thread(de.thread_id());
        }

        match de.info() {
            DebugEventInfo::CreateProcess(_info) => {
                // We pass DEBUG_ONLY_THIS_PROCESS when spawning
                // and will only see the 1 CreateProcess event handled previously.
                unreachable!("Nested targets not supported");
            }

            DebugEventInfo::LoadDll(info) => {
                match self.target.load_module(info.hFile, info.lpBaseOfDll as u64) {
                    Ok(Some(module)) => {
                        callbacks.on_load_dll(self, &module);

                        // We must defer adding any breakpoints until we've seen the initial
                        // breakpoint notification from the OS. Otherwise we may set
                        // breakpoints in startup code before the debugger is properly
                        // initialized.
                        if self.target.saw_initial_bp() {
                            self.apply_module_breakpoints(module.name(), module.base_address())
                        }
                    }
                    Ok(None) => {}
                    Err(e) => {
                        error!("Error loading module: {}", e);
                    }
                }
            }

            DebugEventInfo::UnloadDll(info) => {
                self.target.unload_module(info.lpBaseOfDll as u64);

                callbacks.on_unload_dll(self, info.lpBaseOfDll as u64);
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
                callbacks.on_exit_process(self, info.dwExitCode);
            }

            DebugEventInfo::CreateThread(_info) => {
                callbacks.on_create_thread(self);
            }

            DebugEventInfo::ExitThread(info) => {
                callbacks.on_exit_thread(self, info.dwExitCode);
                self.target.exit_thread(de.thread_id());
            }

            DebugEventInfo::OutputDebugString(info) => {
                // Remove the terminating NUL as it's not needed in a Rust string.
                let length = info.nDebugStringLength.saturating_sub(1) as usize;
                if info.fUnicode != 0 {
                    if let Ok(message) = process::read_wide_string(
                        self.target.process_handle(),
                        info.lpDebugStringData as LPCVOID,
                        length,
                    ) {
                        callbacks.on_output_debug_os_string(self, message);
                    }
                } else {
                    if let Ok(message) = process::read_narrow_string(
                        self.target.process_handle(),
                        info.lpDebugStringData as LPCVOID,
                        length,
                    ) {
                        callbacks.on_output_debug_string(self, message);
                    }
                }
            }

            DebugEventInfo::Rip(info) => {
                callbacks.on_rip(self, info.dwError, info.dwType);
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
                    self.target.set_saw_initial_bp();
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
                self.target.set_saw_initial_wow64_bp();
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
                let process_handle = self.target.process_handle();
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
                            self.target.process_handle(),
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
                                debug!(
                                    "Can't set symbolic breakpoint {}!{}: {}",
                                    module_name.as_ref().display(),
                                    bp.sym,
                                    e
                                );
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
            self.target.process_handle(),
            self.target.current_thread_handle(),
            resolve_symbols,
        );
    }

    pub fn get_module_info(&self, pc: u64) -> Result<ModuleInfo> {
        let dbghelp = dbghelp::lock()?;
        dbghelp.sym_get_module_info(self.target.process_handle(), pc)
    }

    pub fn get_symbol(&self, pc: u64) -> Result<SymInfo> {
        let dbghelp = dbghelp::lock()?;
        dbghelp.sym_from_inline_context(self.target.process_handle(), pc, 0)
    }

    pub fn get_symbol_line_info(&self, pc: u64) -> Result<SymLineInfo> {
        let dbghelp = dbghelp::lock()?;
        dbghelp.sym_get_file_and_line(self.target.process_handle(), pc, 0)
    }

    pub fn get_current_thread_id(&self) -> u64 {
        self.target.current_thread_handle() as u64
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
        process::read_memory_array(self.target.process_handle(), remote_address, buf)?;
        Ok(())
    }

    pub fn get_current_frame(&self) -> Result<StackFrame> {
        let dbghlp = dbghelp::lock()?;

        let mut return_address = ADDRESS64::default();
        let mut stack_pointer = ADDRESS64::default();
        dbghlp.stackwalk_ex(
            self.target.process_handle(),
            self.target.current_thread_handle(),
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
            if target.saw_initial_bp() {
                if target.breakpoint_set_at_addr(exception_address) {
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
            if target.saw_initial_wow64_bp() {
                None
            } else {
                Some(DebuggerNotification::InitialWow64Break)
            }
        }

        EXCEPTION_SINGLE_STEP => {
            if let Some(step_state) = target.single_step(target.current_thread_handle()) {
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
