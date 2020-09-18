// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fmt;

use win_util::process;
use winapi::{
    shared::{
        basetsd::ULONG64,
        minwindef::{LPCVOID, ULONG},
    },
    um::winnt::{EXCEPTION_RECORD, HANDLE},
    STRUCT,
};

use crate::appverifier::stop_codes;

pub const STATUS_VERIFIER_STOP: u32 = ::winapi::shared::ntstatus::STATUS_VERIFIER_STOP as u32;

// VERIFIER_STOP_HEADER and VERIFIER_STOP_PARAMS are not public apis (but probably could be).
// They are defined in os/src/onecore/base/avrf/verifier/logging.h
const MAX_STACK_DEPTH: usize = 32;

STRUCT! {
#[allow(non_snake_case)]
struct VERIFIER_STOP_HEADER {
    StopCode: ULONG64,
    StopFlags: ULONG,
    StackTraceDepth: ULONG,
    BackTrace: [ULONG64; MAX_STACK_DEPTH],
}}

// For our use here, pointers in this struct point to memory in another process, so if you
// want to read those strings, you must use ReadProcessMemory.
STRUCT! {
#[allow(non_snake_case)]
struct VERIFIER_STOP_PARAMS {
   Header: VERIFIER_STOP_HEADER,
   Message: ULONG64,
   Parameter1: ULONG64,
   StringPtr1: ULONG64,
   Parameter2: ULONG64,
   StringPtr2: ULONG64,
   Parameter3: ULONG64,
   StringPtr3: ULONG64,
   Parameter4: ULONG64,
   StringPtr4: ULONG64,
}}

fn handles_stop_from_u32(code: u32) -> HandlesStop {
    match code {
        stop_codes::HANDLES_INVALID_HANDLE => HandlesStop::InvalidHandleStop,
        stop_codes::HANDLES_INVALID_TLS_VALUE => HandlesStop::InvalidTlsValue,
        stop_codes::HANDLES_INCORRECT_WAIT_CALL => HandlesStop::IncorrectWaitCall,
        stop_codes::HANDLES_NULL_HANDLE => HandlesStop::NullHandle,
        stop_codes::HANDLES_WAIT_IN_DLLMAIN => HandlesStop::WaitInDllmain,
        stop_codes::HANDLES_INCORRECT_OBJECT_TYPE => HandlesStop::IncorrectObjectType,
        _ => panic!("Invalid Handles stop code"),
    }
}

fn heap_stop_from_u32(code: u32) -> HeapStop {
    match code {
        stop_codes::HEAPS_UNKNOWN_ERROR => HeapStop::UnknownError,
        stop_codes::HEAPS_ACCESS_VIOLATION => HeapStop::AccessViolation,
        stop_codes::HEAPS_UNSYNCHRONIZED_ACCESS => HeapStop::UnsynchronizedAccess,
        stop_codes::HEAPS_EXTREME_SIZE_REQUEST => HeapStop::ExtremeSizeRequest,
        stop_codes::HEAPS_BAD_HEAP_HANDLE => HeapStop::BadHeapHandle,
        stop_codes::HEAPS_SWITCHED_HEAP_HANDLE => HeapStop::SwitchedHeapHandle,
        stop_codes::HEAPS_DOUBLE_FREE => HeapStop::DoubleFree,
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK => HeapStop::CorruptedHeapBlock,
        stop_codes::HEAPS_DESTROY_PROCESS_HEAP => HeapStop::DestroyProcessHeap,
        stop_codes::HEAPS_UNEXPECTED_EXCEPTION => HeapStop::UnexpectedException,
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_HEADER => {
            HeapStop::CorruptedHeapBlockExceptionRaisedForHeader
        }
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_PROBING => {
            HeapStop::CorruptedHeapBlockExceptionRaisedForProbing
        }
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_HEADER => HeapStop::CorruptedHeapBlockHeader,
        stop_codes::HEAPS_CORRUPTED_FREED_HEAP_BLOCK => HeapStop::CorruptedFreedHeapBlock,
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_SUFFIX => HeapStop::CorruptedHeapBlockSuffix,
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_START_STAMP => {
            HeapStop::CorruptedHeapBlockStartStamp
        }
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_END_STAMP => HeapStop::CorruptedHeapBlockEndStamp,
        stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_PREFIX => HeapStop::CorruptedHeapBlockPrefix,
        stop_codes::HEAPS_FIRST_CHANCE_ACCESS_VIOLATION => HeapStop::FirstChanceAccessViolation,
        stop_codes::HEAPS_CORRUPTED_HEAP_LIST => HeapStop::CorruptedHeapList,
        _ => panic!("unexpected heap stop code"),
    }
}

fn leak_stop_from_u32(code: u32) -> LeakStop {
    match code {
        stop_codes::LEAK_ALLOCATION => LeakStop::Allocation,
        stop_codes::LEAK_HANDLE => LeakStop::Handle,
        stop_codes::LEAK_REGISTRY => LeakStop::Registry,
        stop_codes::LEAK_VIRTUAL_RESERVATION => LeakStop::VirtualReservation,
        stop_codes::LEAK_SYSSTRING => LeakStop::SysString,
        stop_codes::LEAK_POWER_NOTIFICATION => LeakStop::PowerNotification,
        stop_codes::LEAK_COM_ALLOCATION => LeakStop::ComAllocation,
        _ => panic!("unexpected leak stop code"),
    }
}

fn exception_stop_from_u32(code: u32) -> ExceptionStop {
    match code {
        stop_codes::EXCEPTIONS_FIRST_CHANCE_ACCESS_VIOLATION_CODE => {
            ExceptionStop::FirstChanceAccessViolationCode
        }
        _ => panic!("unexpected exception stop code"),
    }
}

/// A bug detected by enabling `handles` in application verifier.
#[derive(Debug)]
pub enum HandlesStop {
    InvalidHandleStop,
    InvalidTlsValue,
    IncorrectWaitCall,
    NullHandle,
    WaitInDllmain,
    IncorrectObjectType,
}

/// A bug detected by enabling `heaps` in application verifier.
#[derive(Debug)]
pub enum HeapStop {
    UnknownError,
    AccessViolation,
    UnsynchronizedAccess,
    ExtremeSizeRequest,
    BadHeapHandle,
    SwitchedHeapHandle,
    DoubleFree,
    CorruptedHeapBlock,
    DestroyProcessHeap,
    UnexpectedException,
    CorruptedHeapBlockExceptionRaisedForHeader,
    CorruptedHeapBlockExceptionRaisedForProbing,
    CorruptedHeapBlockHeader,
    CorruptedFreedHeapBlock,
    CorruptedHeapBlockSuffix,
    CorruptedHeapBlockStartStamp,
    CorruptedHeapBlockEndStamp,
    CorruptedHeapBlockPrefix,
    FirstChanceAccessViolation,
    CorruptedHeapList,
}

/// A bug detected by enabling `leak` in application verifier.
#[derive(Debug)]
pub enum LeakStop {
    Allocation,
    Handle,
    Registry,
    VirtualReservation,
    SysString,
    PowerNotification,
    ComAllocation,
}

/// A bug detected by enabling `exceptions` in application verifier.
///
/// We don't enable this option normally because it only detects first chance exceptions which are already
/// reported and this option ends up reporting the same issue a second time with a different stack.
#[derive(Debug)]
pub enum ExceptionStop {
    FirstChanceAccessViolationCode,
}

/// A verifier stop has a specific exception code but the exception parameters provide additional useful
/// information in understanding the type of bug detected.
///
/// This message encapsulates the most important kinds of bugs detected by application verifier when fuzzing.
pub enum VerifierStop {
    /// A bug detected by enabling `heaps` in application verifier.
    Heap(HeapStop),

    /// A bug detected by enabling `handles` in application verifier.
    Handles(HandlesStop),

    /// A bug detected by enabling `leak` in application verifier.
    Leak(LeakStop),

    /// A bug detected by enabling `exceptions` in application verifier.
    Exception(ExceptionStop),

    /// A bug was detected by a currently unsupported option in application verifier.
    Unknown,
}

impl fmt::Display for VerifierStop {
    fn fmt(&self, formatter: &mut fmt::Formatter) -> fmt::Result {
        match self {
            VerifierStop::Heap(code) => write!(formatter, "{:?}", code),
            VerifierStop::Handles(code) => write!(formatter, "{:?}", code),
            VerifierStop::Leak(code) => write!(formatter, "{:?}", code),
            VerifierStop::Exception(code) => write!(formatter, "{:?}", code),
            VerifierStop::Unknown => write!(formatter, "Unknown"),
        }
    }
}

pub fn new(process_handle: HANDLE, exception_record: &EXCEPTION_RECORD) -> VerifierStop {
    if exception_record.NumberParameters >= 3 {
        match process::read_memory::<VERIFIER_STOP_PARAMS>(
            process_handle,
            exception_record.ExceptionInformation[2] as LPCVOID,
        ) {
            Ok(stop_params) => {
                let code = stop_params.Header.StopCode as u32;
                match code {
                    stop_codes::HANDLES_INVALID_HANDLE
                        ..=stop_codes::HANDLES_INCORRECT_OBJECT_TYPE => {
                        let handles_stop = handles_stop_from_u32(code);
                        VerifierStop::Handles(handles_stop)
                    }
                    stop_codes::HEAPS_UNKNOWN_ERROR..=stop_codes::HEAPS_CORRUPTED_HEAP_LIST => {
                        let heap_stop = heap_stop_from_u32(code);
                        VerifierStop::Heap(heap_stop)
                    }
                    stop_codes::LEAK_ALLOCATION..=stop_codes::LEAK_COM_ALLOCATION => {
                        let leak_stop = leak_stop_from_u32(code);
                        VerifierStop::Leak(leak_stop)
                    }
                    stop_codes::EXCEPTIONS_FIRST_CHANCE_ACCESS_VIOLATION_CODE => {
                        let exception_stop = exception_stop_from_u32(code);
                        VerifierStop::Exception(exception_stop)
                    }
                    _ => VerifierStop::Unknown,
                }
            }
            Err(_) => VerifierStop::Unknown,
        }
    } else {
        VerifierStop::Unknown
    }
}
