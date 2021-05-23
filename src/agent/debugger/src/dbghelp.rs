// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This is only needed because of the types defined here that are missing from winapi.
// Once they get added to winapi, this should be removed.
#![allow(bad_style)]
#![allow(clippy::unreadable_literal)]
#![allow(clippy::collapsible_if)]
#![allow(clippy::needless_return)]
#![allow(clippy::upper_case_acronyms)]

/// This module defines a wrapper around dbghelp apis so they can be used in a thread safe manner
/// as well as providing a more Rust like api.
use std::{
    cmp,
    ffi::{OsStr, OsString},
    mem::{size_of, MaybeUninit},
    num::NonZeroU64,
    path::{Path, PathBuf},
    sync::Once,
};

use anyhow::{Context, Result};
use log::warn;
use win_util::{check_winapi, last_os_error, process};
use winapi::{
    shared::{
        basetsd::{DWORD64, PDWORD64},
        guiddef::GUID,
        minwindef::{BOOL, DWORD, FALSE, LPVOID, MAX_PATH, PDWORD, TRUE, ULONG, WORD},
        ntdef::{PCWSTR, PWSTR},
        winerror::{ERROR_ALREADY_EXISTS, ERROR_SUCCESS},
    },
    um::{
        dbghelp::{
            AddrModeFlat, StackWalkEx, SymCleanup, SymFindFileInPathW, SymFromNameW,
            SymFunctionTableAccess64, SymGetModuleBase64, SymInitializeW, SymLoadModuleExW,
            IMAGEHLP_LINEW64, INLINE_FRAME_CONTEXT_IGNORE, INLINE_FRAME_CONTEXT_INIT,
            PIMAGEHLP_LINEW64, PSYMBOL_INFOW, STACKFRAME_EX, SYMBOL_INFOW, SYMOPT_DEBUG,
            SYMOPT_DEFERRED_LOADS, SYMOPT_FAIL_CRITICAL_ERRORS, SYMOPT_NO_PROMPTS,
            SYM_STKWALK_DEFAULT,
        },
        errhandlingapi::GetLastError,
        handleapi::CloseHandle,
        processthreadsapi::{GetThreadContext, SetThreadContext},
        synchapi::{CreateMutexA, ReleaseMutex, WaitForSingleObjectEx},
        winbase::{
            Wow64GetThreadContext, Wow64SetThreadContext, INFINITE, WAIT_ABANDONED, WAIT_FAILED,
        },
        winnt::{
            CONTEXT, CONTEXT_ALL, HANDLE, IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE_I386, WCHAR,
            WOW64_CONTEXT, WOW64_CONTEXT_ALL,
        },
    },
    ENUM, STRUCT,
};

// We use 4096 based on C4503 - the documented VC++ warning that a name is truncated.
const MAX_SYM_NAME: usize = 4096;

// Arbitrary practical choice, but must not exceed `u32::MAX`.
const MAX_SYM_SEARCH_PATH_LEN: usize = 8192;

/// For `flags` parameter of `SymFindFileInPath`.
///
/// Missing from `winapi-rs`.
const SSRVOPT_DWORD: DWORD = 0x0002;

// Ideally this would be a function, but it would require returning a large stack
// allocated object **and** an interior pointer to the object, so we use a macro instead.
macro_rules! init_sym_info {
    ($symbol_info: ident) => {{
        // We must allocate enough space for the SYMBOL_INFOW **and** the maximum
        // number of wide (2 byte) characters at the end of the SYMBOL_INFOW struct.
        const MIN_SYMINFO_SIZE: usize = 2 * MAX_SYM_NAME + size_of::<SYMBOL_INFOW>();

        // The macro caller provides the name of the local variable that we initialize.
        // This odd pattern is used so we can return an interior pointer within this aligned
        // stack allocation.
        $symbol_info = Aligned8([0u8; MIN_SYMINFO_SIZE]);
        let aligned_sym_info = &mut $symbol_info.0;

        // Clippy isn't smart enough to know the first field of our aligned struct is also aligned.
        #[allow(clippy::cast_ptr_alignment)]
        let symbol_info_ptr = unsafe { &mut *(aligned_sym_info.as_mut_ptr() as *mut SYMBOL_INFOW) };
        symbol_info_ptr.MaxNameLen = MAX_SYM_NAME as ULONG;

        // the struct size not counting the variable length name.
        symbol_info_ptr.SizeOfStruct = size_of::<SYMBOL_INFOW>() as DWORD;
        symbol_info_ptr
    }};
}

/// We use dbghlp Sym apis to walk a stack. dbghlp apis are documented as not being thread safe,
/// so we provide a lock around our use of these apis.
///
/// Note that Rust itself also uses dbghlp to get a stack trace, e.g. when you panic and set
/// RUST_BACKTRACE.
///
/// This function is based on the `backtrace` crate which is also used in Rust std. Here
/// we use the same named local mutex to hopefully avoid any unsynchronized uses of dbghlp
/// in std.
pub fn lock() -> Result<DebugHelpGuard> {
    use core::sync::atomic::{AtomicUsize, Ordering};

    static LOCK: AtomicUsize = AtomicUsize::new(0);
    let mut lock = LOCK.load(Ordering::SeqCst);
    if lock == 0 {
        lock = unsafe {
            CreateMutexA(
                std::ptr::null_mut(),
                0,
                "Local\\RustBacktraceMutex\0".as_ptr() as _,
            ) as usize
        };

        if lock == 0 {
            return Err(last_os_error());
        }

        // Handle the race between threads creating our mutex by closing ours if another
        // thread created the mutex first.
        if let Err(other) = LOCK.compare_exchange(0, lock, Ordering::SeqCst, Ordering::SeqCst) {
            debug_assert_ne!(other, 0);
            debug_assert_eq!(unsafe { GetLastError() }, ERROR_ALREADY_EXISTS);
            unsafe { CloseHandle(lock as HANDLE) };
            lock = other;
        }
    }
    debug_assert_ne!(lock, 0);
    let lock = lock as HANDLE;
    match unsafe { WaitForSingleObjectEx(lock, INFINITE, FALSE) } {
        WAIT_FAILED => return Err(last_os_error()),
        WAIT_ABANDONED => {
            warn!("dbghlp mutex was abandoned");
        }
        _ => {}
    }

    let dbghlp = DebugHelpGuard::new(lock);

    static DBGHLP_INIT: Once = Once::new();
    DBGHLP_INIT.call_once(|| {
        // Set SYMOPT_DEFERRED_LOADS for performance.
        // Set SYMOPT_FAIL_CRITICAL_ERRORS and SYMOPT_NO_PROMPTS to avoid popups.
        dbghlp.sym_set_options(
            dbghlp.sym_get_options()
                | SYMOPT_DEBUG
                | SYMOPT_DEFERRED_LOADS
                | SYMOPT_FAIL_CRITICAL_ERRORS
                | SYMOPT_NO_PROMPTS,
        );
    });

    Ok(dbghlp)
}

// Not defined in winapi yet
ENUM! {enum SYM_TYPE {
    SymNone = 0,
    SymCoff,
    SymCv,
    SymPdb,
    SymExport,
    SymDeferred,
    SymSym,
    SymDia,
    SymVirtual,
    NumSymTypes,
}}
STRUCT! {struct IMAGEHLP_MODULEW64 {
    SizeOfStruct: DWORD,
    BaseOfImage: DWORD64,
    ImageSize: DWORD,
    TimeDateStamp: DWORD,
    CheckSum: DWORD,
    NumSyms: DWORD,
    SymType: SYM_TYPE,
    ModuleName: [WCHAR; 32],
    ImageName: [WCHAR; 256],
    LoadedImageName: [WCHAR; 256],
    LoadedPdbName: [WCHAR; 256],
    CVSig: DWORD,
    CVData: [WCHAR; MAX_PATH * 3],
    PdbSig: DWORD,
    PdbSig70: GUID,
    PdbAge: DWORD,
    PdbUnmatched: BOOL,
    DbgUnmatched: BOOL,
    LineNumbers: BOOL,
    GlobalSymbols: BOOL,
    TypeInfo: BOOL,
    SourceIndexed: BOOL,
    Publics: BOOL,
    MachineType: DWORD,
    Reserved: DWORD,
}}
pub type PIMAGEHLP_MODULEW64 = *mut IMAGEHLP_MODULEW64;

// Not defined in winapi yet
extern "system" {
    pub fn SymGetOptions() -> DWORD;
    pub fn SymSetOptions(_: DWORD) -> DWORD;
    pub fn SymFromInlineContextW(
        hProcess: HANDLE,
        Address: DWORD64,
        InlineContext: ULONG,
        Displacement: PDWORD64,
        Symbol: PSYMBOL_INFOW,
    ) -> BOOL;
    pub fn SymGetLineFromInlineContextW(
        hProcess: HANDLE,
        dwAddr: DWORD64,
        InlineContext: ULONG,
        qwModuleBaseAddress: DWORD64,
        pdwDisplacement: PDWORD,
        Line: PIMAGEHLP_LINEW64,
    ) -> BOOL;
    pub fn SymGetModuleInfoW64(
        hProcess: HANDLE,
        qwAddr: DWORD64,
        ModuleInfo: PIMAGEHLP_MODULEW64,
    ) -> BOOL;
    pub fn SymGetSearchPathW(hProcess: HANDLE, SearchPath: PWSTR, SearchPathLength: DWORD) -> BOOL;
    pub fn SymSetSearchPathW(hProcess: HANDLE, SearchPath: PCWSTR) -> BOOL;
}

#[repr(C, align(8))]
struct Aligned8<T>(T);

#[repr(C, align(16))]
pub struct Aligned16<T>(T);

#[allow(clippy::large_enum_variant)]
pub enum FrameContext {
    X64(Aligned16<CONTEXT>),
    X86(WOW64_CONTEXT),
}

impl FrameContext {
    pub fn program_counter(&self) -> u64 {
        match self {
            FrameContext::X64(ctx) => ctx.0.Rip,
            FrameContext::X86(ctx) => ctx.Eip as u64,
        }
    }

    pub fn set_program_counter(&mut self, ip: u64) {
        match self {
            FrameContext::X64(ctx) => {
                ctx.0.Rip = ip;
            }
            FrameContext::X86(ctx) => {
                ctx.Eip = ip as u32;
            }
        }
    }

    pub fn stack_pointer(&self) -> u64 {
        match self {
            FrameContext::X64(ctx) => ctx.0.Rsp,
            FrameContext::X86(ctx) => ctx.Esp as u64,
        }
    }

    pub fn frame_pointer(&self) -> u64 {
        match self {
            FrameContext::X64(ctx) => ctx.0.Rbp,
            FrameContext::X86(ctx) => ctx.Ebp as u64,
        }
    }

    pub fn set_single_step(&mut self, enable: bool) {
        const TRAP_FLAG: u32 = 1 << 8;

        let flags = match self {
            FrameContext::X64(ctx) => &mut ctx.0.EFlags,
            FrameContext::X86(ctx) => &mut ctx.EFlags,
        };

        if enable {
            *flags |= TRAP_FLAG;
        } else {
            *flags &= !TRAP_FLAG;
        }
    }

    pub fn as_mut_ptr(&mut self) -> LPVOID {
        match self {
            FrameContext::X64(ctx) => &mut ctx.0 as *mut CONTEXT as LPVOID,
            FrameContext::X86(ctx) => ctx as *mut WOW64_CONTEXT as LPVOID,
        }
    }

    pub fn machine_type(&self) -> WORD {
        match self {
            FrameContext::X64(_) => IMAGE_FILE_MACHINE_AMD64,
            FrameContext::X86(_) => IMAGE_FILE_MACHINE_I386,
        }
    }

    pub fn set_thread_context(&self, thread_handle: HANDLE) -> Result<()> {
        match self {
            FrameContext::X86(ctx) => {
                check_winapi(|| unsafe { Wow64SetThreadContext(thread_handle, ctx) })
                    .context("SetThreadContext")?
            }
            FrameContext::X64(ctx) => {
                check_winapi(|| unsafe { SetThreadContext(thread_handle, &ctx.0) })
                    .context("SetThreadContext")?
            }
        }

        Ok(())
    }

    pub fn get_register_u64<R: Into<iced_x86::Register>>(&self, reg: R) -> u64 {
        use iced_x86::Register::*;

        let reg = reg.into();
        let full_register_value = match self {
            FrameContext::X64(cr) => match reg.full_register() {
                RIP => cr.0.Rip,
                RAX => cr.0.Rax,
                RBX => cr.0.Rbx,
                RCX => cr.0.Rcx,
                RDX => cr.0.Rdx,
                R8 => cr.0.R8,
                R9 => cr.0.R9,
                R10 => cr.0.R10,
                R11 => cr.0.R11,
                R12 => cr.0.R12,
                R13 => cr.0.R13,
                R14 => cr.0.R14,
                R15 => cr.0.R15,
                RDI => cr.0.Rdi,
                RSI => cr.0.Rsi,
                RBP => cr.0.Rbp,
                RSP => cr.0.Rsp,
                CS | DS | ES | FS | SS => 0u64,
                // GS points to the TEB in 64b but there is no official documented way
                // to return GS.
                // Unofficially: https://www.geoffchappell.com/studies/windows/win32/ntdll/structs/teb/index.htm
                // But for now, return 0.
                GS => 0u64,
                _ => unimplemented!("Register read {:?}", reg),
            },

            FrameContext::X86(cr) => match reg {
                EIP => cr.Eip as u64,
                EAX => cr.Eax as u64,
                EBX => cr.Ebx as u64,
                ECX => cr.Ecx as u64,
                EDX => cr.Edx as u64,
                EDI => cr.Edi as u64,
                ESI => cr.Esi as u64,
                EBP => cr.Ebp as u64,
                ESP => cr.Esp as u64,
                _ => unimplemented!("Register read {:?}", reg),
            },
        };

        match reg {
            EAX | EBX | ECX | EDX | R8D | R9D | R10D | R11D | R12D | R13D | R14D | R15D | EDI
            | ESI | EBP | ESP => full_register_value & 0x0000_0000_ffff_ffff,

            AX | BX | CX | DX | R8W | R9W | R10W | R11W | R12W | R13W | R14W | R15W | DI | SI
            | BP | SP => full_register_value & 0x0000_0000_0000_ffff,

            AL | BL | CL | DL | R8L | R9L | R10L | R11L | R12L | R13L | R14L | R15L | DIL | SIL
            | BPL | SPL => full_register_value & 0x0000_0000_0000_00ff,

            AH | BH | CH | DH => (full_register_value & 0x0000_ff00) >> 8,

            _ => full_register_value as u64,
        }
    }

    pub fn get_flags(&self) -> u32 {
        match self {
            FrameContext::X64(cr) => cr.0.EFlags,
            FrameContext::X86(cr) => cr.EFlags,
        }
    }
}

pub fn get_thread_frame(process_handle: HANDLE, thread_handle: HANDLE) -> Result<FrameContext> {
    if process::is_wow64_process(process_handle) {
        let mut ctx: WOW64_CONTEXT = unsafe { MaybeUninit::zeroed().assume_init() };
        ctx.ContextFlags = WOW64_CONTEXT_ALL;

        check_winapi(|| unsafe { Wow64GetThreadContext(thread_handle, &mut ctx) })
            .context("Wow64GetThreadContext")?;
        Ok(FrameContext::X86(ctx))
    } else {
        // required by `CONTEXT`, is a FIXME in winapi right now
        let mut ctx: Aligned16<CONTEXT> = unsafe { MaybeUninit::zeroed().assume_init() };

        ctx.0.ContextFlags = CONTEXT_ALL;
        check_winapi(|| unsafe { GetThreadContext(thread_handle, &mut ctx.0) })
            .context("GetThreadContext")?;
        Ok(FrameContext::X64(ctx))
    }
}

pub struct ModuleInfo {
    name: OsString,
    base_address: u64,
}

impl ModuleInfo {
    pub fn name(&self) -> &OsStr {
        &self.name
    }

    pub fn base_address(&self) -> u64 {
        self.base_address
    }
}

#[derive(Clone, Debug, Hash, PartialEq)]
pub struct SymInfo {
    pub symbol: String,
    pub address: u64,
    pub displacement: u64,
}

impl SymInfo {
    /// Return the name of the symbol.
    pub fn symbol(&self) -> &str {
        &self.symbol
    }

    /// Return the address of the symbol.
    pub fn address(&self) -> u64 {
        self.address
    }

    /// Return the displacement from the address of the symbol.
    pub fn displacement(&self) -> u64 {
        self.displacement
    }
}

pub struct SymLineInfo {
    filename: PathBuf,
    line_number: u32,
}

// Magic line numbers that have special meaning in the debugger.
// If we see these, we don't use the line number, instead we report the offset.
const STEP_LINE_OVER: u32 = 0x00f00f00;
const STEP_LINE_THRU: u32 = 0x00feefee;

impl SymLineInfo {
    pub fn filename(&self) -> &Path {
        &self.filename
    }

    pub fn line_number(&self) -> u32 {
        self.line_number
    }

    pub fn is_fake_line_number(&self) -> bool {
        self.line_number == STEP_LINE_OVER || self.line_number == STEP_LINE_THRU
    }
}

pub struct DebugHelpGuard {
    lock: HANDLE,
}

impl DebugHelpGuard {
    pub fn new(lock: HANDLE) -> Self {
        DebugHelpGuard { lock }
    }

    pub fn sym_get_options(&self) -> DWORD {
        unsafe { SymGetOptions() }
    }

    pub fn sym_set_options(&self, options: DWORD) -> DWORD {
        unsafe { SymSetOptions(options) }
    }

    pub fn sym_initialize(&self, process_handle: HANDLE) -> Result<()> {
        check_winapi(|| unsafe { SymInitializeW(process_handle, std::ptr::null(), FALSE) })
    }

    pub fn sym_cleanup(&self, process_handle: HANDLE) -> Result<()> {
        check_winapi(|| unsafe { SymCleanup(process_handle) })
    }

    pub fn sym_load_module(
        &self,
        process_handle: HANDLE,
        file_handle: HANDLE,
        image_name: &Path,
        base_of_dll: DWORD64,
        image_size: u32,
    ) -> Result<DWORD64> {
        let load_address = unsafe {
            SymLoadModuleExW(
                process_handle,
                file_handle,
                win_util::string::to_wstring(image_name).as_ptr(),
                std::ptr::null_mut(),
                base_of_dll,
                image_size,
                std::ptr::null_mut(),
                0,
            )
        };

        match load_address {
            0 => {
                // If the dll was already loaded, don't return an error. This can happen
                // when we have multiple debuggers - each tracks loading symbols separately.
                let last_error = std::io::Error::last_os_error();
                match last_error.raw_os_error() {
                    Some(code) if code == ERROR_SUCCESS as i32 => Ok(0),
                    _ => Err(last_error.into()),
                }
            }
            _ => Ok(load_address),
        }
    }

    pub fn get_module_base(&self, process_handle: HANDLE, addr: DWORD64) -> Result<NonZeroU64> {
        if let Some(base) = NonZeroU64::new(unsafe { SymGetModuleBase64(process_handle, addr) }) {
            Ok(base)
        } else {
            let last_error = std::io::Error::last_os_error();
            Err(last_error.into())
        }
    }

    pub fn stackwalk_ex<F: FnMut(&STACKFRAME_EX) -> bool>(
        &self,
        process_handle: HANDLE,
        thread_handle: HANDLE,
        walk_inline_frames: bool,
        mut f: F,
    ) -> Result<()> {
        let mut frame_context = get_thread_frame(process_handle, thread_handle)?;

        let mut frame: STACKFRAME_EX = unsafe { MaybeUninit::zeroed().assume_init() };
        frame.AddrPC.Offset = frame_context.program_counter();
        frame.AddrPC.Mode = AddrModeFlat;
        frame.AddrStack.Offset = frame_context.stack_pointer();
        frame.AddrStack.Mode = AddrModeFlat;
        frame.AddrFrame.Offset = frame_context.frame_pointer();
        frame.AddrFrame.Mode = AddrModeFlat;
        frame.InlineFrameContext = if walk_inline_frames {
            INLINE_FRAME_CONTEXT_INIT
        } else {
            INLINE_FRAME_CONTEXT_IGNORE
        };

        loop {
            let success = unsafe {
                StackWalkEx(
                    frame_context.machine_type().into(),
                    process_handle,
                    thread_handle,
                    &mut frame,
                    frame_context.as_mut_ptr(),
                    None,
                    Some(SymFunctionTableAccess64),
                    Some(SymGetModuleBase64),
                    None,
                    SYM_STKWALK_DEFAULT,
                )
            };

            if success != TRUE {
                break;
            }

            if !f(&frame) {
                break;
            }
        }

        Ok(())
    }

    pub fn sym_from_inline_context(
        &self,
        process_handle: HANDLE,
        program_counter: u64,
        inline_context: DWORD,
    ) -> Result<SymInfo> {
        let mut sym_info;
        let sym_info_ptr = init_sym_info!(sym_info);

        let mut displacement = 0;
        check_winapi(|| unsafe {
            SymFromInlineContextW(
                process_handle,
                program_counter,
                inline_context,
                &mut displacement,
                sym_info_ptr,
            )
        })?;

        let address = sym_info_ptr.Address;
        let name_len = cmp::min(
            sym_info_ptr.NameLen as usize,
            sym_info_ptr.MaxNameLen as usize - 1,
        );
        let name_ptr = sym_info_ptr.Name.as_ptr() as *const u16;
        let name = unsafe { std::slice::from_raw_parts(name_ptr, name_len) };
        let symbol = String::from_utf16_lossy(name);

        Ok(SymInfo {
            symbol,
            address,
            displacement,
        })
    }

    pub fn sym_get_file_and_line(
        &self,
        process_handle: HANDLE,
        program_counter: u64,
        inline_context: DWORD,
    ) -> Result<SymLineInfo> {
        let mut line_info: IMAGEHLP_LINEW64 = unsafe { MaybeUninit::zeroed().assume_init() };
        line_info.SizeOfStruct = size_of::<IMAGEHLP_LINEW64>() as DWORD;
        let mut displacement: DWORD = 0;
        check_winapi(|| unsafe {
            SymGetLineFromInlineContextW(
                process_handle,
                program_counter,
                inline_context,
                0,
                &mut displacement,
                &mut line_info,
            )
        })?;

        let filename = unsafe { win_util::string::os_string_from_wide_ptr(line_info.FileName) };
        Ok(SymLineInfo {
            filename: filename.into(),
            line_number: line_info.LineNumber,
        })
    }

    pub fn sym_get_module_info(
        &self,
        process_handle: HANDLE,
        program_counter: u64,
    ) -> Result<ModuleInfo> {
        let mut module_info: IMAGEHLP_MODULEW64 = unsafe { MaybeUninit::zeroed().assume_init() };
        module_info.SizeOfStruct = size_of::<IMAGEHLP_MODULEW64>() as DWORD;
        check_winapi(|| unsafe {
            SymGetModuleInfoW64(process_handle, program_counter, &mut module_info)
        })?;

        let module_name =
            unsafe { win_util::string::os_string_from_wide_ptr(module_info.ModuleName.as_ptr()) };

        Ok(ModuleInfo {
            name: module_name,
            base_address: module_info.BaseOfImage,
        })
    }

    pub fn sym_from_name(
        &self,
        process_handle: HANDLE,
        modname: impl AsRef<Path>,
        sym: &str,
    ) -> Result<SymInfo> {
        assert!(sym.len() + 1 < MAX_SYM_NAME);
        let mut sym_info;
        let sym_info_ptr = init_sym_info!(sym_info);

        let mut qualified_sym = OsString::from(modname.as_ref());
        qualified_sym.push("!");
        qualified_sym.push(sym);
        check_winapi(|| unsafe {
            SymFromNameW(
                process_handle,
                win_util::string::to_wstring(qualified_sym).as_ptr(),
                sym_info_ptr,
            )
        })?;

        Ok(SymInfo {
            symbol: sym.to_string(),
            address: sym_info_ptr.Address,
            displacement: 0,
        })
    }

    /// Look for a filesystem path to a PDB file using the symbol handler's
    /// current search path.
    ///
    /// This method is effectively a specialization of `SymFindFileInPathW`.
    ///
    /// Note: `file_name` may be a full path, but only the file name is used.
    pub fn find_pdb_file_in_path(
        &self,
        process_handle: HANDLE,
        file_name: impl AsRef<Path>,
        pdb_signature: u32,
        pdb_age: u32,
    ) -> Result<Option<PathBuf>> {
        let file_name = win_util::string::to_wstring(file_name);

        // Must be at least `MAX_PATH` characters in length.
        let mut found_file_data = Vec::<u16>::with_capacity(MAX_PATH);

        // Inherit search path used in `SymInitializeW()`. When that is also set
        // to `NULL`, the default search path is used.
        let search_path = std::ptr::null_mut();

        // See: https://docs.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-symfindfileinpathw#remarks
        let id = pdb_signature as *mut _;
        let two = pdb_age;
        let three = 0;

        // Assert that we are passing a DWORD signature in `id`.
        let flags = SSRVOPT_DWORD;

        let result = check_winapi(|| unsafe {
            SymFindFileInPathW(
                process_handle,
                search_path,
                file_name.as_ptr(),
                id,
                two,
                three,
                flags,
                found_file_data.as_mut_ptr(),
                None,
                std::ptr::null_mut(),
            )
        });

        if result.is_ok() {
            // Safety: `found_file_data` must contain at least one NUL byte.
            //
            // We zero-initialize `found_file_data`, and assume that `SymFindFileInPathW`
            // only succeeds if it wrote a NUL-terminated wide string.
            let found_file =
                unsafe { win_util::string::os_string_from_wide_ptr(found_file_data.as_ptr()) };

            Ok(Some(found_file.into()))

        } else {
            Ok(None)

        }
    }

    pub fn sym_get_search_path(&self, process_handle: HANDLE) -> Result<OsString> {
        let mut search_path_data = Vec::<u16>::with_capacity(MAX_SYM_SEARCH_PATH_LEN);
        let search_path_len = MAX_SYM_SEARCH_PATH_LEN as u32;
        check_winapi(|| unsafe {
            SymGetSearchPathW(
                process_handle,
                search_path_data.as_mut_ptr(),
                search_path_len,
            )
        })?;

        // Safety: `search_path_data` must contain at least one NUL byte.
        //
        // We zero-initialize `search_path_data`, and assume that `SymGetSearchPathW`
        // only succeeds if it wrote a NUL-terminated wide string.
        let search_path =
            unsafe { win_util::string::os_string_from_wide_ptr(search_path_data.as_ptr()) };

        Ok(search_path)
    }

    pub fn sym_set_search_path(
        &self,
        process_handle: HANDLE,
        search_path: impl AsRef<OsStr>,
    ) -> Result<()> {
        let mut search_path = win_util::string::to_wstring(search_path.as_ref());

        check_winapi(|| unsafe { SymSetSearchPathW(process_handle, search_path.as_mut_ptr()) })?;

        Ok(())
    }
}

impl Drop for DebugHelpGuard {
    fn drop(&mut self) {
        let r = unsafe { ReleaseMutex(self.lock) };
        debug_assert!(r != 0);
    }
}
