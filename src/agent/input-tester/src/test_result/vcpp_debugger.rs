// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::unreadable_literal)]

use std::ffi::c_void;

/// This module wraps an exception raised by the VC++ runtime or by user code implementing
/// https://docs.microsoft.com/en-us/visualstudio/debugger/how-to-set-a-thread-name-in-native-code?view=vs-2019
///
/// Some uses of this exception are documented, some are not. The FFI here is derived from
/// tagEXCEPTION_VISUALCPP_DEBUG_INFO (defined in multiple places, e.g. vscommon\dbghelper\dbghlper.h)
/// but we extract the fields explicitly from the exception record parameters to deal with
/// layouts that differ between x86 and x64 (our target process could be either).
use anyhow::Result;
use win_util::process;
use windows::{
    core::{PCSTR, PCWSTR},
    Win32::{
        Foundation::{BOOL, HANDLE, NTSTATUS},
        System::Diagnostics::Debug::EXCEPTION_RECORD,
    },
};

/// Errors reported via the VC++ /RTC compiler flag, as defined in vctruntime\inc\rtcapi.h
#[derive(Copy, Clone, Debug)]
pub enum VcppRtcError {
    UnknownRtcError,
    /// _RTC_CORRUPT_STACK (stack memory corrupted)
    CorruptStack,
    /// _RTC_CORRUPTED_ALLOCA (stack memory around allloca corrupted)
    CorruptAlloca,
    /// _RTC_UNINIT_LOCAL_USE (local variable used before initialized)
    UseUninitializedVariable,
    /// _RTC_CHKSTK (ESP not saved properly across a function call (usually calling convention error)
    StackPointerCorrupted,
    /// _RTC_CVRT_LOSS_INFO (cast to smaller data type - not always a bug)
    ShorteningConvertDataLoss,
}

// this is a special exception used to:
//   * name a thread
//   * report rtc errors
// we should not report any bugs if the exception was raised to name a thread.
pub const EXCEPTION_VISUALCPP_DEBUGGER: NTSTATUS = NTSTATUS(0x406d1388);

// The exception is a simple notification to the debugger which can be ignored.
const EXCEPTION_DEBUGGER_NAME_THREAD: u32 = 0x1000;
// The exception is asking the debugger if it is aware of RTC errors. If the debugger
// is aware, it will modify the memory of the target which will then raise a RUNTIMECHECK
// exception. We should do this eventually (PBI #6530).
const EXCEPTION_DEBUGGER_PROBE: u32 = 0x1001;
// This exception is raised only after the PROBE exception and if the debugger set the
// target memory to raise this exception.
const EXCEPTION_DEBUGGER_RUNTIMECHECK: u32 = 0x1002;
// Unsure if this is used at all, but there is info to extract
const EXCEPTION_DEBUGGER_FIBER: u32 = 0x1003;
// Defined in the vc headers, but no info to extract and no uses.
//const EXCEPTION_DEBUGGER_HANDLECHECK: DWORD = 0x1004;

#[allow(non_snake_case)]
pub struct ThreadNameInfo {
    /// pointer to name (in user addr space)
    pub szName: PCSTR,
    /// thread id (-1=caller thread)
    pub dwThreadId: u32,
    /// reserved for future use (eg user thread, system thread)
    pub dwFlags: u32,
}

#[allow(non_snake_case)]
pub struct DebuggerProbeInfo {
    /// 0 = do you understand this private exception, else max value of enum
    pub dwLevelRequired: u32,
    /// debugger puts a non-zero value in this address to tell runtime debugger is aware of RTC
    pub pbDebuggerPresent: *mut c_void,
}

impl DebuggerProbeInfo {
    pub fn notify_target(&self, process_handle: HANDLE) -> Result<()> {
        // This will tell the VC++ runtime to raise another exception to report the error.
        process::write_memory(process_handle, self.pbDebuggerPresent, &1u8)
    }
}

// Based on _RTC_ErrorNumber, used in the dwRuntimeNumber field
const RTC_CHKSTK: u32 = 0;
const RTC_CVRT_LOSS_INFO: u32 = 1;
const RTC_CORRUPT_STACK: u32 = 2;
const RTC_UNINIT_LOCAL_USE: u32 = 3;
const RTC_CORRUPTED_ALLOCA: u32 = 4;

#[allow(non_snake_case)]
pub struct RuntimeErrorInfo {
    /// the type of the runtime check
    pub dwRuntimeNumber: u32,
    /// true if never a false-positive
    pub bRealBug: BOOL,
    /// caller puts a return address in here
    pub pvReturnAddress: *mut c_void,
    /// debugger puts a non-zero value in this address if handled it
    pub pbDebuggerPresent: *mut c_void,
    /// pointer to unicode message (or null)
    pub pwRuntimeMessage: PCWSTR,
}

impl RuntimeErrorInfo {
    pub fn notify_target(&self, process_handle: HANDLE) -> Result<()> {
        // This will tell the VC++ runtime to **not** use __debugbreak() to report the error.
        process::write_memory(process_handle, self.pbDebuggerPresent, &1u8)
    }

    pub fn get_rtc_error(&self) -> VcppRtcError {
        match self.dwRuntimeNumber {
            RTC_CHKSTK => VcppRtcError::StackPointerCorrupted,
            RTC_CVRT_LOSS_INFO => VcppRtcError::ShorteningConvertDataLoss,
            RTC_CORRUPT_STACK => VcppRtcError::CorruptStack,
            RTC_UNINIT_LOCAL_USE => VcppRtcError::UseUninitializedVariable,
            RTC_CORRUPTED_ALLOCA => VcppRtcError::CorruptAlloca,
            _ => VcppRtcError::UnknownRtcError,
        }
    }
}

#[allow(non_snake_case)]
pub struct FiberInfo {
    /// 0=ConvertThreadToFiber, 1=CreateFiber, 2=DeleteFiber
    pub dwType: u32,
    /// pointer to fiber
    pub pvFiber: *mut c_void,
    /// pointer to FIBER_START_ROUTINE (CreateFiber only)
    pub pvStartRoutine: *mut c_void,
}

pub enum VcppDebuggerExceptionInfo {
    ThreadName(ThreadNameInfo),
    Probe(DebuggerProbeInfo),
    RuntimeError(RuntimeErrorInfo),
    Fiber(FiberInfo),
    UnknownException,
}

impl VcppDebuggerExceptionInfo {
    pub fn from_exception_record(exception_record: &EXCEPTION_RECORD, target_x64: bool) -> Self {
        assert_eq!(exception_record.ExceptionCode, EXCEPTION_VISUALCPP_DEBUGGER);

        if exception_record.NumberParameters == 0 {
            return VcppDebuggerExceptionInfo::UnknownException;
        }

        match exception_record.ExceptionInformation[0] as u32 {
            EXCEPTION_DEBUGGER_NAME_THREAD
                if target_x64 && exception_record.NumberParameters >= 3 =>
            {
                VcppDebuggerExceptionInfo::ThreadName(ThreadNameInfo {
                    szName: PCSTR(exception_record.ExceptionInformation[1] as _),
                    dwThreadId: exception_record.ExceptionInformation[2] as u32,
                    dwFlags: (exception_record.ExceptionInformation[2] >> 32) as u32,
                })
            }

            EXCEPTION_DEBUGGER_NAME_THREAD
                if !target_x64 && exception_record.NumberParameters >= 4 =>
            {
                VcppDebuggerExceptionInfo::ThreadName(ThreadNameInfo {
                    szName: PCSTR(exception_record.ExceptionInformation[1] as _),
                    dwThreadId: exception_record.ExceptionInformation[2] as u32,
                    dwFlags: exception_record.ExceptionInformation[3] as u32,
                })
            }

            EXCEPTION_DEBUGGER_PROBE if exception_record.NumberParameters >= 3 => {
                VcppDebuggerExceptionInfo::Probe(DebuggerProbeInfo {
                    dwLevelRequired: exception_record.ExceptionInformation[1] as u32,
                    pbDebuggerPresent: exception_record.ExceptionInformation[2] as *mut c_void,
                })
            }

            EXCEPTION_DEBUGGER_RUNTIMECHECK
                if target_x64 && exception_record.NumberParameters >= 6 =>
            {
                VcppDebuggerExceptionInfo::RuntimeError(RuntimeErrorInfo {
                    dwRuntimeNumber: exception_record.ExceptionInformation[1] as u32,
                    bRealBug: BOOL(exception_record.ExceptionInformation[2] as _),
                    pvReturnAddress: exception_record.ExceptionInformation[3] as *mut c_void,
                    pbDebuggerPresent: exception_record.ExceptionInformation[4] as *mut c_void,
                    pwRuntimeMessage: PCWSTR(exception_record.ExceptionInformation[5] as _),
                })
            }

            EXCEPTION_DEBUGGER_RUNTIMECHECK
                if !target_x64 && exception_record.NumberParameters >= 5 =>
            {
                VcppDebuggerExceptionInfo::RuntimeError(RuntimeErrorInfo {
                    dwRuntimeNumber: exception_record.ExceptionInformation[1] as u32,
                    bRealBug: BOOL((exception_record.ExceptionInformation[1] >> 32) as _),
                    pvReturnAddress: exception_record.ExceptionInformation[2] as *mut c_void,
                    pbDebuggerPresent: exception_record.ExceptionInformation[3] as *mut c_void,
                    pwRuntimeMessage: PCWSTR(exception_record.ExceptionInformation[4] as _),
                })
            }

            EXCEPTION_DEBUGGER_FIBER if exception_record.NumberParameters >= 4 => {
                VcppDebuggerExceptionInfo::Fiber(FiberInfo {
                    dwType: exception_record.ExceptionInformation[1] as u32,
                    pvFiber: exception_record.ExceptionInformation[2] as *mut c_void,
                    pvStartRoutine: exception_record.ExceptionInformation[3] as *mut c_void,
                })
            }

            _ => VcppDebuggerExceptionInfo::UnknownException,
        }
    }
}
