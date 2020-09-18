// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! This module implements a Rust wrapper around the Win32 DEBUG_EVENT.
use std::{
    self,
    fmt::{self, Display},
    path::Path,
};

use win_util::file::get_path_from_handle;
use winapi::{
    shared::minwindef::DWORD,
    um::minwinbase::{
        CREATE_PROCESS_DEBUG_EVENT, CREATE_PROCESS_DEBUG_INFO, CREATE_THREAD_DEBUG_EVENT,
        CREATE_THREAD_DEBUG_INFO, DEBUG_EVENT, EXCEPTION_DEBUG_EVENT, EXCEPTION_DEBUG_INFO,
        EXIT_PROCESS_DEBUG_EVENT, EXIT_PROCESS_DEBUG_INFO, EXIT_THREAD_DEBUG_EVENT,
        EXIT_THREAD_DEBUG_INFO, LOAD_DLL_DEBUG_EVENT, LOAD_DLL_DEBUG_INFO,
        OUTPUT_DEBUG_STRING_EVENT, OUTPUT_DEBUG_STRING_INFO, RIP_EVENT, RIP_INFO,
        UNLOAD_DLL_DEBUG_EVENT, UNLOAD_DLL_DEBUG_INFO,
    },
};

pub enum DebugEventInfo<'a> {
    Exception(&'a EXCEPTION_DEBUG_INFO),
    CreateThread(&'a CREATE_THREAD_DEBUG_INFO),
    CreateProcess(&'a CREATE_PROCESS_DEBUG_INFO),
    ExitThread(&'a EXIT_THREAD_DEBUG_INFO),
    ExitProcess(&'a EXIT_PROCESS_DEBUG_INFO),
    LoadDll(&'a LOAD_DLL_DEBUG_INFO),
    UnloadDll(&'a UNLOAD_DLL_DEBUG_INFO),
    OutputDebugString(&'a OUTPUT_DEBUG_STRING_INFO),
    Rip(&'a RIP_INFO),
    Unknown,
}

impl<'a> Display for DebugEventInfo<'a> {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        use DebugEventInfo::*;
        match self {
            Exception(info) => {
                write!(
                    formatter,
                    "event=Exception exception_code=0x{:08x} exception_address=0x{:08x} first_chance={}",
                    info.ExceptionRecord.ExceptionCode,
                    info.ExceptionRecord.ExceptionAddress as u64,
                    info.dwFirstChance != 0
                )?;
            }
            CreateThread(_info) => {
                write!(formatter, "event=CreateThread")?;
            }
            CreateProcess(info) => {
                let image_name = get_path_from_handle(info.hFile).unwrap_or_else(|_| "???".into());
                write!(
                    formatter,
                    "event=CreateProcess name={} base=0x{:016x}",
                    Path::new(&image_name).display(),
                    info.lpBaseOfImage as u64,
                )?;
            }
            ExitThread(info) => {
                write!(formatter, "event=ExitThread exit_code={}", info.dwExitCode)?;
            }
            ExitProcess(info) => {
                write!(formatter, "event=ExitProcess exit_code={}", info.dwExitCode)?;
            }
            LoadDll(info) => {
                let image_name = get_path_from_handle(info.hFile).unwrap_or_else(|_| "???".into());
                write!(
                    formatter,
                    "event=LoadDll name={} base=0x{:016x}",
                    Path::new(&image_name).display(),
                    info.lpBaseOfDll as u64,
                )?;
            }
            UnloadDll(info) => {
                write!(
                    formatter,
                    "event=UnloadDll base=0x{:016x}",
                    info.lpBaseOfDll as u64,
                )?;
            }
            OutputDebugString(info) => {
                write!(
                    formatter,
                    "event=OutputDebugString unicode={} address=0x{:016x} length={}",
                    info.fUnicode, info.lpDebugStringData as u64, info.nDebugStringLength,
                )?;
            }
            Rip(info) => {
                write!(
                    formatter,
                    "event=Rip error=0x{:x} type={}",
                    info.dwError, info.dwType
                )?;
            }
            Unknown => {
                write!(formatter, "event=Unknown")?;
            }
        };

        Ok(())
    }
}

pub struct DebugEvent<'a> {
    process_id: DWORD,
    thread_id: DWORD,
    info: DebugEventInfo<'a>,
}

impl<'a> DebugEvent<'a> {
    pub fn new(de: &'a DEBUG_EVENT) -> Self {
        let info = unsafe {
            match de.dwDebugEventCode {
                EXCEPTION_DEBUG_EVENT => DebugEventInfo::Exception(de.u.Exception()),
                CREATE_PROCESS_DEBUG_EVENT => {
                    DebugEventInfo::CreateProcess(de.u.CreateProcessInfo())
                }
                CREATE_THREAD_DEBUG_EVENT => DebugEventInfo::CreateThread(de.u.CreateThread()),
                EXIT_PROCESS_DEBUG_EVENT => DebugEventInfo::ExitProcess(de.u.ExitProcess()),
                EXIT_THREAD_DEBUG_EVENT => DebugEventInfo::ExitThread(de.u.ExitThread()),
                LOAD_DLL_DEBUG_EVENT => DebugEventInfo::LoadDll(de.u.LoadDll()),
                UNLOAD_DLL_DEBUG_EVENT => DebugEventInfo::UnloadDll(de.u.UnloadDll()),
                OUTPUT_DEBUG_STRING_EVENT => DebugEventInfo::OutputDebugString(de.u.DebugString()),
                RIP_EVENT => DebugEventInfo::Rip(de.u.RipInfo()),
                _ => DebugEventInfo::Unknown,
            }
        };

        Self {
            process_id: de.dwProcessId,
            thread_id: de.dwThreadId,
            info,
        }
    }

    pub fn process_id(&self) -> DWORD {
        self.process_id
    }

    pub fn thread_id(&self) -> DWORD {
        self.thread_id
    }

    pub fn info(&self) -> &DebugEventInfo {
        &self.info
    }
}

impl<'a> Display for DebugEvent<'a> {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        write!(
            formatter,
            " pid={} tid={} {}",
            self.process_id, self.thread_id, self.info
        )?;
        Ok(())
    }
}
