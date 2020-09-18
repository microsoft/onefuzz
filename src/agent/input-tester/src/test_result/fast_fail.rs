// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::unreadable_literal)]

use winapi::um::winnt::{
    EXCEPTION_RECORD, FAST_FAIL_APCS_DISABLED, FAST_FAIL_CERTIFICATION_FAILURE,
    FAST_FAIL_CORRUPT_LIST_ENTRY, FAST_FAIL_CRYPTO_LIBRARY, FAST_FAIL_DEPRECATED_SERVICE_INVOKED,
    FAST_FAIL_DLOAD_PROTECTION_FAILURE, FAST_FAIL_FATAL_APP_EXIT, FAST_FAIL_GS_COOKIE_INIT,
    FAST_FAIL_GUARD_EXPORT_SUPPRESSION_FAILURE, FAST_FAIL_GUARD_ICALL_CHECK_FAILURE,
    FAST_FAIL_GUARD_ICALL_CHECK_SUPPRESSED, FAST_FAIL_GUARD_JUMPTABLE, FAST_FAIL_GUARD_SS_FAILURE,
    FAST_FAIL_GUARD_WRITE_CHECK_FAILURE, FAST_FAIL_INCORRECT_STACK, FAST_FAIL_INVALID_ARG,
    FAST_FAIL_INVALID_BALANCED_TREE, FAST_FAIL_INVALID_BUFFER_ACCESS,
    FAST_FAIL_INVALID_CALL_IN_DLL_CALLOUT, FAST_FAIL_INVALID_CONTROL_STACK,
    FAST_FAIL_INVALID_DISPATCH_CONTEXT, FAST_FAIL_INVALID_EXCEPTION_CHAIN,
    FAST_FAIL_INVALID_FIBER_SWITCH, FAST_FAIL_INVALID_FILE_OPERATION, FAST_FAIL_INVALID_IDLE_STATE,
    FAST_FAIL_INVALID_IMAGE_BASE, FAST_FAIL_INVALID_JUMP_BUFFER, FAST_FAIL_INVALID_LOCK_STATE,
    FAST_FAIL_INVALID_LONGJUMP_TARGET, FAST_FAIL_INVALID_NEXT_THREAD,
    FAST_FAIL_INVALID_REFERENCE_COUNT, FAST_FAIL_INVALID_SET_OF_CONTEXT,
    FAST_FAIL_INVALID_SYSCALL_NUMBER, FAST_FAIL_INVALID_THREAD, FAST_FAIL_LEGACY_GS_VIOLATION,
    FAST_FAIL_LOADER_CONTINUITY_FAILURE, FAST_FAIL_LPAC_ACCESS_DENIED, FAST_FAIL_MRDATA_MODIFIED,
    FAST_FAIL_MRDATA_PROTECTION_FAILURE, FAST_FAIL_RANGE_CHECK_FAILURE,
    FAST_FAIL_SET_CONTEXT_DENIED, FAST_FAIL_STACK_COOKIE_CHECK_FAILURE,
    FAST_FAIL_UNEXPECTED_HEAP_EXCEPTION, FAST_FAIL_UNSAFE_EXTENSION_CALL,
    FAST_FAIL_UNSAFE_REGISTRY_ACCESS, FAST_FAIL_VTGUARD_CHECK_FAILURE,
};

/// The C compiler intrinsic __fastfail was called with one of these values - we use UnknownFastFailCode for values
/// we have not seen before.
///
/// See https://docs.microsoft.com/en-us/cpp/intrinsics/fastfail?view=vs-2017 for __fastfail details.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum FastFail {
    UnknownFastFailCode,
    LegacyGsViolation,
    VtguardCheckFailure,
    StackCookieCheckFailure,
    CorruptListEntry,
    IncorrectStack,
    InvalidArg,
    GsCookieInit,
    FatalAppExit,
    RangeCheckFailure,
    UnsafeRegistryAccess,
    GuardIcallCheckFailure,
    GuardWriteCheckFailure,
    InvalidFiberSwitch,
    InvalidSetOfContext,
    InvalidReferenceCount,
    InvalidJumpBuffer,
    MrdataModified,
    CertificationFailure,
    InvalidExceptionChain,
    CryptoLibrary,
    InvalidCallInDllCallout,
    InvalidImageBase,
    DloadProtectionFailure,
    UnsafeExtensionCall,
    DeprecatedServiceInvoked,
    InvalidBufferAccess,
    InvalidBalancedTree,
    InvalidNextThread,
    GuardIcallCheckSuppressed,
    ApcsDisabled,
    InvalidIdleState,
    MrdataProtectionFailure,
    UnexpectedHeapException,
    InvalidLockState,
    GuardJumptable,
    InvalidLongjumpTarget,
    InvalidDispatchContext,
    InvalidThread,
    InvalidSyscallNumber,
    InvalidFileOperation,
    LpacAccessDenied,
    GuardSsFailure,
    LoaderContinuityFailure,
    GuardExportSuppressionFailure,
    InvalidControlStack,
    SetContextDenied,
}

// See https://docs.microsoft.com/en-us/cpp/intrinsics/fastfail?view=vs-2017 for __fastfail details.
pub const EXCEPTION_FAIL_FAST: u32 = 0xC0000409;

fn fast_fail_from_u32(code: u32) -> FastFail {
    match code {
        FAST_FAIL_LEGACY_GS_VIOLATION => FastFail::LegacyGsViolation,
        FAST_FAIL_VTGUARD_CHECK_FAILURE => FastFail::VtguardCheckFailure,
        FAST_FAIL_STACK_COOKIE_CHECK_FAILURE => FastFail::StackCookieCheckFailure,
        FAST_FAIL_CORRUPT_LIST_ENTRY => FastFail::CorruptListEntry,
        FAST_FAIL_INCORRECT_STACK => FastFail::IncorrectStack,
        FAST_FAIL_INVALID_ARG => FastFail::InvalidArg,
        FAST_FAIL_GS_COOKIE_INIT => FastFail::GsCookieInit,
        FAST_FAIL_FATAL_APP_EXIT => FastFail::FatalAppExit,
        FAST_FAIL_RANGE_CHECK_FAILURE => FastFail::RangeCheckFailure,
        FAST_FAIL_UNSAFE_REGISTRY_ACCESS => FastFail::UnsafeRegistryAccess,
        FAST_FAIL_GUARD_ICALL_CHECK_FAILURE => FastFail::GuardIcallCheckFailure,
        FAST_FAIL_GUARD_WRITE_CHECK_FAILURE => FastFail::GuardWriteCheckFailure,
        FAST_FAIL_INVALID_FIBER_SWITCH => FastFail::InvalidFiberSwitch,
        FAST_FAIL_INVALID_SET_OF_CONTEXT => FastFail::InvalidSetOfContext,
        FAST_FAIL_INVALID_REFERENCE_COUNT => FastFail::InvalidReferenceCount,
        FAST_FAIL_INVALID_JUMP_BUFFER => FastFail::InvalidJumpBuffer,
        FAST_FAIL_MRDATA_MODIFIED => FastFail::MrdataModified,
        FAST_FAIL_CERTIFICATION_FAILURE => FastFail::CertificationFailure,
        FAST_FAIL_INVALID_EXCEPTION_CHAIN => FastFail::InvalidExceptionChain,
        FAST_FAIL_CRYPTO_LIBRARY => FastFail::CryptoLibrary,
        FAST_FAIL_INVALID_CALL_IN_DLL_CALLOUT => FastFail::InvalidCallInDllCallout,
        FAST_FAIL_INVALID_IMAGE_BASE => FastFail::InvalidImageBase,
        FAST_FAIL_DLOAD_PROTECTION_FAILURE => FastFail::DloadProtectionFailure,
        FAST_FAIL_UNSAFE_EXTENSION_CALL => FastFail::UnsafeExtensionCall,
        FAST_FAIL_DEPRECATED_SERVICE_INVOKED => FastFail::DeprecatedServiceInvoked,
        FAST_FAIL_INVALID_BUFFER_ACCESS => FastFail::InvalidBufferAccess,
        FAST_FAIL_INVALID_BALANCED_TREE => FastFail::InvalidBalancedTree,
        FAST_FAIL_INVALID_NEXT_THREAD => FastFail::InvalidNextThread,
        FAST_FAIL_GUARD_ICALL_CHECK_SUPPRESSED => FastFail::GuardIcallCheckSuppressed,
        FAST_FAIL_APCS_DISABLED => FastFail::ApcsDisabled,
        FAST_FAIL_INVALID_IDLE_STATE => FastFail::InvalidIdleState,
        FAST_FAIL_MRDATA_PROTECTION_FAILURE => FastFail::MrdataProtectionFailure,
        FAST_FAIL_UNEXPECTED_HEAP_EXCEPTION => FastFail::UnexpectedHeapException,
        FAST_FAIL_INVALID_LOCK_STATE => FastFail::InvalidLockState,
        FAST_FAIL_GUARD_JUMPTABLE => FastFail::GuardJumptable,
        FAST_FAIL_INVALID_LONGJUMP_TARGET => FastFail::InvalidLongjumpTarget,
        FAST_FAIL_INVALID_DISPATCH_CONTEXT => FastFail::InvalidDispatchContext,
        FAST_FAIL_INVALID_THREAD => FastFail::InvalidThread,
        FAST_FAIL_INVALID_SYSCALL_NUMBER => FastFail::InvalidSyscallNumber,
        FAST_FAIL_INVALID_FILE_OPERATION => FastFail::InvalidFileOperation,
        FAST_FAIL_LPAC_ACCESS_DENIED => FastFail::LpacAccessDenied,
        FAST_FAIL_GUARD_SS_FAILURE => FastFail::GuardSsFailure,
        FAST_FAIL_LOADER_CONTINUITY_FAILURE => FastFail::LoaderContinuityFailure,
        FAST_FAIL_GUARD_EXPORT_SUPPRESSION_FAILURE => FastFail::GuardExportSuppressionFailure,
        FAST_FAIL_INVALID_CONTROL_STACK => FastFail::InvalidControlStack,
        FAST_FAIL_SET_CONTEXT_DENIED => FastFail::SetContextDenied,
        _ => FastFail::UnknownFastFailCode,
    }
}

pub fn from_exception_record(exception_record: &EXCEPTION_RECORD) -> FastFail {
    if exception_record.NumberParameters == 1 {
        fast_fail_from_u32(exception_record.ExceptionInformation[0] as u32)
    } else {
        FastFail::UnknownFastFailCode
    }
}
