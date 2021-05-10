// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::unreadable_literal)]

pub mod asan;
pub mod fast_fail;
pub mod vcpp_debugger;
pub mod verifier_stop;

use std::{fmt, path::Path};

use debugger::stack::{DebugStack, DebugStackFrame};
use log::error;
use win_util::process;
use winapi::um::{
    minwinbase::{
        EXCEPTION_ACCESS_VIOLATION, EXCEPTION_ARRAY_BOUNDS_EXCEEDED, EXCEPTION_BREAKPOINT,
        EXCEPTION_DATATYPE_MISALIGNMENT, EXCEPTION_DEBUG_INFO, EXCEPTION_FLT_DENORMAL_OPERAND,
        EXCEPTION_FLT_DIVIDE_BY_ZERO, EXCEPTION_FLT_INEXACT_RESULT,
        EXCEPTION_FLT_INVALID_OPERATION, EXCEPTION_FLT_OVERFLOW, EXCEPTION_FLT_STACK_CHECK,
        EXCEPTION_FLT_UNDERFLOW, EXCEPTION_ILLEGAL_INSTRUCTION, EXCEPTION_INT_DIVIDE_BY_ZERO,
        EXCEPTION_INT_OVERFLOW, EXCEPTION_INVALID_DISPOSITION, EXCEPTION_IN_PAGE_ERROR,
        EXCEPTION_NONCONTINUABLE_EXCEPTION, EXCEPTION_PRIV_INSTRUCTION, EXCEPTION_SINGLE_STEP,
        EXCEPTION_STACK_OVERFLOW,
    },
    winnt::{EXCEPTION_RECORD, HANDLE},
};

use crate::{
    crash_detector::DebuggerResult,
    test_result::{
        asan::{asan_error_from_exception_record, AsanError, EH_SANITIZER},
        fast_fail::{FastFail, EXCEPTION_FAIL_FAST},
        vcpp_debugger::{VcppDebuggerExceptionInfo, VcppRtcError},
        verifier_stop::{VerifierStop, STATUS_VERIFIER_STOP},
    },
};

// See https://github.com/dotnet/coreclr/blob/030a3ea9b8dbeae89c90d34441d4d9a1cf4a7de6/src/inc/corexcep.h#L21
const EXCEPTION_CLR: u32 = 0xE0434352;

// From vc crt source file ehdata_values.h
// #define EH_EXCEPTION_NUMBER  ('msc' | 0xE0000000)    // The NT Exception # that we use
// Also defined here: https://github.com/dotnet/coreclr/blob/030a3ea9b8dbeae89c90d34441d4d9a1cf4a7de6/src/inc/corexcep.h#L19
const EXCEPTION_CPP: u32 = 0xE06D7363;

// When debugging a WoW64 process, we see STATUS_WX86_BREAKPOINT in addition to EXCEPTION_BREAKPOINT
const STATUS_WX86_BREAKPOINT: u32 = ::winapi::shared::ntstatus::STATUS_WX86_BREAKPOINT as u32;

fn get_av_description(exception_record: &EXCEPTION_RECORD) -> ExceptionCode {
    if exception_record.NumberParameters >= 2 {
        let write = exception_record.ExceptionInformation[0] != 0;
        let null = exception_record.ExceptionInformation[1] == 0;
        match (write, null) {
            (true, true) => ExceptionCode::WriteToNull,
            (true, false) => ExceptionCode::WriteAccessViolation,
            (false, true) => ExceptionCode::ReadFromNull,
            (false, false) => ExceptionCode::ReadAccessViolation,
        }
    } else {
        ExceptionCode::UnknownAccessViolation
    }
}

fn generic_exception(exception_record: &EXCEPTION_RECORD) -> Option<ExceptionCode> {
    match exception_record.ExceptionCode {
        EXCEPTION_ACCESS_VIOLATION => Some(get_av_description(exception_record)),
        EXCEPTION_ARRAY_BOUNDS_EXCEEDED => Some(ExceptionCode::ArrayBoundsExceeded),
        // EXCEPTION_BREAKPOINT - when the debugger bitness matches the debuggee
        // STATUS_WX86_BREAKPOINT - when the debugger is 64 bit and the debuggee is Wow64.
        // In other words, the exception code is a debugger implementation detail, the end
        // user only really cares that it was a breakpoint.
        EXCEPTION_BREAKPOINT | STATUS_WX86_BREAKPOINT => Some(ExceptionCode::Breakpoint),
        EXCEPTION_DATATYPE_MISALIGNMENT => Some(ExceptionCode::MisalignedData),
        EXCEPTION_FLT_DENORMAL_OPERAND => Some(ExceptionCode::FltDenormalOperand),
        EXCEPTION_FLT_DIVIDE_BY_ZERO => Some(ExceptionCode::FltDivByZero),
        EXCEPTION_FLT_INEXACT_RESULT => Some(ExceptionCode::FltInexactResult),
        EXCEPTION_FLT_INVALID_OPERATION => Some(ExceptionCode::FltInvalidOperation),
        EXCEPTION_FLT_OVERFLOW => Some(ExceptionCode::FltOverflow),
        EXCEPTION_FLT_STACK_CHECK => Some(ExceptionCode::FltStackCheck),
        EXCEPTION_FLT_UNDERFLOW => Some(ExceptionCode::FltUnderflow),
        EXCEPTION_ILLEGAL_INSTRUCTION => Some(ExceptionCode::IllegalInstruction),
        EXCEPTION_INT_DIVIDE_BY_ZERO => Some(ExceptionCode::IntDivByZero),
        EXCEPTION_INT_OVERFLOW => Some(ExceptionCode::IntOverflow),
        EXCEPTION_INVALID_DISPOSITION => Some(ExceptionCode::InvalidDisposition),
        EXCEPTION_IN_PAGE_ERROR => Some(ExceptionCode::InPageError),
        EXCEPTION_NONCONTINUABLE_EXCEPTION => Some(ExceptionCode::NonContinuableException),
        EXCEPTION_PRIV_INSTRUCTION => Some(ExceptionCode::PrivilegedInstruction),
        EXCEPTION_SINGLE_STEP => Some(ExceptionCode::SingleStep),
        EXCEPTION_STACK_OVERFLOW => Some(ExceptionCode::StackOverflow),
        EXCEPTION_CLR => Some(ExceptionCode::ClrException),
        EXCEPTION_CPP => Some(ExceptionCode::CppException),
        _ => None,
    }
}

/// A friendly description of the exception based on the exception code and other
/// parameters available to the debugger when the exception was raised.
#[derive(Clone)]
pub enum ExceptionDescription {
    /// A generic exception with no additional details.
    GenericException(ExceptionCode),

    /// An exception detected by enabling application verifier.
    VerifierStop(VerifierStop),

    /// An exception raised by calling __fastfail.
    FastFail(FastFail),

    /// An exception detected by ASAN.
    Asan(AsanError),

    /// An exception detected by the VC++ RTC checks
    Rtc(VcppRtcError),
}

impl fmt::Display for ExceptionDescription {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        match self {
            ExceptionDescription::GenericException(code) => write!(formatter, "{:?}", code),
            ExceptionDescription::VerifierStop(stop) => write!(formatter, "VerifierStop({})", stop),
            ExceptionDescription::FastFail(code) => write!(formatter, "FastFail({:?})", code),
            ExceptionDescription::Asan(code) => write!(formatter, "{:?}", code),
            ExceptionDescription::Rtc(code) => write!(formatter, "{:?}", code),
        }
    }
}

pub fn new_exception_description(
    process_handle: HANDLE,
    exception_record: &EXCEPTION_RECORD,
) -> ExceptionDescription {
    if let Some(generic_exception) = generic_exception(exception_record) {
        ExceptionDescription::GenericException(generic_exception)
    } else {
        match exception_record.ExceptionCode {
            EXCEPTION_FAIL_FAST => {
                ExceptionDescription::FastFail(fast_fail::from_exception_record(exception_record))
            }
            STATUS_VERIFIER_STOP => ExceptionDescription::VerifierStop(verifier_stop::new(
                process_handle,
                exception_record,
            )),
            EH_SANITIZER => ExceptionDescription::Asan(asan_error_from_exception_record(
                process_handle,
                exception_record,
            )),
            vcpp_debugger::EXCEPTION_VISUALCPP_DEBUGGER => {
                if let VcppDebuggerExceptionInfo::RuntimeError(info) =
                    VcppDebuggerExceptionInfo::from_exception_record(
                        exception_record,
                        !process::is_wow64_process(process_handle),
                    )
                {
                    if let Err(e) = info.notify_target(process_handle) {
                        error!("Error notifying target on vcpp runtime error: {}", e);
                    }
                    ExceptionDescription::Rtc(info.get_rtc_error())
                } else {
                    ExceptionDescription::GenericException(ExceptionCode::UnknownExceptionCode)
                }
            }
            _ => ExceptionDescription::GenericException(ExceptionCode::UnknownExceptionCode),
        }
    }
}

pub fn new_exception(
    process_handle: HANDLE,
    exception: &EXCEPTION_DEBUG_INFO,
    stack: DebugStack,
) -> Exception {
    let stack_hash = stack.stable_hash();
    Exception {
        exception_code: exception.ExceptionRecord.ExceptionCode,
        description: new_exception_description(process_handle, &exception.ExceptionRecord),
        stack_hash,
        first_chance: exception.dwFirstChance != 0,
        stack_frames: stack.frames,
    }
}

pub fn new_test_result(
    debugger_result: DebuggerResult,
    input_file: &Path,
    log_path: &Path,
) -> TestResult {
    TestResult {
        bugs: debugger_result.exceptions,
        input_file: input_file.to_string_lossy().to_string(),
        log_path: format!("{}", log_path.display()),
        debugger_output: debugger_result.debugger_output,
        test_stdout: debugger_result.stdout,
        test_stderr: debugger_result.stderr,
        exit_status: debugger_result.exit_status,
        covered_blocks: debugger_result
            .coverage
            .as_ref()
            .map_or(0, |c| c.covered_blocks()),
    }
}

/// The details of an exception observed by the execution engine.
#[derive(Clone)]
pub struct Exception {
    /// The win32 exception code.
    pub exception_code: u32,

    /// A friendly description of the exception based on the exception code and other
    /// parameters available to the debugger when the exception was raised.
    pub description: ExceptionDescription,

    /// A hash of the call stack when the exception was raised.
    pub stack_hash: u64,

    /// True if the exception if "first chance". Applications can handle first chance exceptions,
    /// so it is possible to see more than one. When `first_chance` is false, the exception caused
    /// the program to crash.
    pub first_chance: bool,

    /// The call stack when the exception was raised.
    pub stack_frames: Vec<DebugStackFrame>,
}

impl fmt::Display for Exception {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        writeln!(formatter, "Exception: 0x{:8x}", self.exception_code)?;
        writeln!(formatter, "    Description: {}", self.description)?;
        writeln!(formatter, "    FirstChance: {}", self.first_chance)?;
        writeln!(formatter, "    StackHash: {}", self.stack_hash)?;
        writeln!(formatter, "    Stack:")?;
        for frame in &self.stack_frames {
            writeln!(formatter, "        {}", frame)?;
        }
        Ok(())
    }
}

/// How did the program exit - normally (so we have a proper exit code) or was it terminated?
#[derive(Copy, Clone, Debug, PartialEq)]
pub enum ExitStatus {
    /// The exit code returned from the process.
    Code(i32),

    /// Unix only - terminated by signal.
    Signal(i32),

    /// The application took longer than the maximum allowed and was terminated, timeout is in seconds.
    Timeout(u64),
}

impl ExitStatus {
    pub fn from_code(code: i32) -> Self {
        ExitStatus::Code(code)
    }

    pub fn from_timeout(timeout_s: u64) -> Self {
        ExitStatus::Timeout(timeout_s)
    }

    pub fn is_normal_exit(&self) -> bool {
        matches!(self, ExitStatus::Code(_))
    }

    pub fn is_timeout(&self) -> bool {
        matches!(self, ExitStatus::Timeout(_))
    }
}

impl fmt::Display for ExitStatus {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        match self {
            ExitStatus::Code(c) => write!(formatter, "Exit code: {}", c),
            ExitStatus::Signal(c) => write!(formatter, "Signal: {}", c),
            ExitStatus::Timeout(sec) => write!(formatter, "Timeout: {}s", sec),
        }
    }
}

/// A fuzzer or execution engine sends this message to a back end to report the bugs found for a single input.
#[derive(Clone)]
pub struct TestResult {
    /// The input filename that results in the bugs_found.
    pub input_file: String,

    /// The standard output from running the program (possibly merged with stderr which would then be empty).
    pub test_stdout: String,

    /// The standard output from running the program (possibly merged with stderr which would then be empty).
    pub test_stderr: String,

    /// The output from the debugger attached to the program.
    pub debugger_output: String,

    /// The bugs found when testing input_file.
    pub bugs: Vec<Exception>,

    /// A log file or directory that should be shared with the customer along with the input_file and bug details.
    pub log_path: String,

    /// How did the program exit - normally (so we have a proper exit code) or was it terminated?
    pub exit_status: ExitStatus,

    /// Count of blocks covered
    pub covered_blocks: u64,
}

impl TestResult {
    pub fn any_crashes(&self) -> bool {
        !self.bugs.is_empty()
    }

    pub fn timed_out(&self) -> bool {
        self.exit_status.is_timeout()
    }

    pub fn any_crashes_or_timed_out(&self) -> bool {
        self.any_crashes() || self.timed_out()
    }
}

/// This is a non-exhaustive list of exceptions that might be raised in a program.
#[derive(Copy, Clone, Debug)]
pub enum ExceptionCode {
    UnknownExceptionCode,
    UnknownApplicationVerifierStop,
    UnknownFastFail,
    /// A read reference to an invalid address (null)
    ReadFromNull,
    /// A write reference to an invalid address (null)
    WriteToNull,
    /// A read reference to an invalid address (non-null)
    ReadAccessViolation,
    /// A write reference to an invalid address (non-null)
    WriteAccessViolation,
    /// A read or write reference to an invalid address where we don't know the address.
    UnknownAccessViolation,
    ArrayBoundsExceeded,
    MisalignedData,
    Breakpoint,
    SingleStep,
    BoundsExceeded,
    FltDenormalOperand,
    FltDivByZero,
    FltInexactResult,
    FltInvalidOperation,
    FltOverflow,
    FltStackCheck,
    FltUnderflow,
    IntDivByZero,
    IntOverflow,
    PrivilegedInstruction,
    InPageError,
    IllegalInstruction,
    NonContinuableException,
    StackOverflow,
    InvalidDisposition,
    GuardPage,
    InvalidHandleException,
    PossibleDeadlock,
    /// An exception raised from .Net code
    ClrException,
    /// An exception raised from the C++ throw statement.
    CppException,
    /// An error detected by ASAN.
    Asan,
}
