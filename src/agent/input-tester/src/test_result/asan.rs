// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::unreadable_literal)]

use std::mem::size_of;

use anyhow::{bail, Result};
use win_util::process;
use win_util::UNION; // Ideally this would be exported from winapi.
use winapi::{
    shared::{
        basetsd::UINT64,
        minwindef::{DWORD, LPCVOID},
        ntdef::LPWSTR,
    },
    um::winnt::{EXCEPTION_RECORD, HANDLE},
    STRUCT,
};

/// An error detected by ASAN.
///
/// These kinds are based on strings output by the lldb ASAN plugin, unrecognized errors should use `UnknownAsanError`.
#[derive(Debug)]
pub enum AsanError {
    UnknownAsanError,
    HeapUseAfterFree,
    HeapBufferOverflow,
    StackBufferUnderflow,
    InitializationOrderFiasco,
    StackBufferOverflow,
    StackUseAfterReturn,
    UseAfterPoison,
    ContainerOverflow,
    StackUseAfterScope,
    GlobalBufferOverflow,
    UnknownCrash,
    /// Leading underscore to avoid conflicts because protoc generates C++ enums which share the namespace.
    StackOverflow,
    NullDeref,
    WildJump,
    WildAddrWrite,
    WildAddrRead,
    WildAddr,
    Signal,
    /// Leading underscore to avoid conflicts because protoc generates C++ enums which share the namespace.
    DoubleFree,
    NewDeleteTypeMismatch,
    BadFree,
    AllocDeallocMismatch,
    ParamOverlap,
    NegativeSizeParam,
    InvalidPointerPair,
}

// Types defined in vcasan.h
STRUCT! {
#[allow(non_snake_case)]
struct EXCEPTION_ASAN_ERROR {
    // The description string from asan, such as heap-use-after-free
    uiRuntimeDescriptionLength: UINT64,
    pwRuntimeDescription: LPWSTR,

    // A translation of the description string to something more user friendly done by this lib
    // not localized
    uiRuntimeShortMessageLength: UINT64,
    pwRuntimeShortMessage: LPWSTR,

    // the full report from asan, not localized
    uiRuntimeFullMessageLength: UINT64,
    pwRuntimeFullMessage: LPWSTR, /* pointer to Unicode message (or NULL) */

    // azure payload, WIP
    uiCustomDataLength: UINT64,
    pwCustomData: LPWSTR,
}}

UNION! {
union EXCEPTION_SANITIZER_ERROR_u {
    [u64; 8],
    asan asan_mut: EXCEPTION_ASAN_ERROR,
}}

STRUCT! {
#[allow(non_snake_case)]
struct EXCEPTION_SANITIZER_ERROR {
    // the size of this structure, set by the caller
    cbSize: DWORD,
    // the specific type of sanitizer error this is. Set by the caller, determines which member of the union is valid
    dwSanitizerKind: DWORD,
    u: EXCEPTION_SANITIZER_ERROR_u,
}}

// #define EH_SANITIZER        ('san' | 0xE0000000)
// #define EH_SANITIZER_ASAN   (EH_SANITIZER + 1)
pub const EH_SANITIZER: u32 =
    0xe0000000 | ((b's' as u32) << 16) | ((b'a' as u32) << 8) | b'n' as u32; // 0xe073616e;
pub const EH_SANITIZER_ASAN: u32 = EH_SANITIZER + 1;

fn get_exception_sanitizer_error(
    process_handle: HANDLE,
    remote_asan_error: LPCVOID,
) -> Result<EXCEPTION_SANITIZER_ERROR> {
    let record =
        process::read_memory::<EXCEPTION_SANITIZER_ERROR>(process_handle, remote_asan_error)?;
    if record.dwSanitizerKind != EH_SANITIZER_ASAN {
        anyhow::bail!("Unrecognized sanitizer kind");
    }
    if (record.cbSize as usize) < size_of::<EXCEPTION_SANITIZER_ERROR>() {
        anyhow::bail!("Unrecognized sanitizer record size");
    }
    Ok(record)
}

fn get_runtime_description(process_handle: HANDLE, remote_asan_error: LPCVOID) -> Result<String> {
    let record = get_exception_sanitizer_error(process_handle, remote_asan_error)?;
    let asan_error = unsafe { record.u.asan() };
    let size = asan_error.uiRuntimeDescriptionLength as usize;
    let remote_message_address = asan_error.pwRuntimeDescription as LPCVOID;
    let message = process::read_wide_string(process_handle, remote_message_address, size)?;
    Ok(message.to_string_lossy().to_string())
}

fn get_full_message(process_handle: HANDLE, remote_asan_error: LPCVOID) -> Result<String> {
    let record = get_exception_sanitizer_error(process_handle, remote_asan_error)?;
    let asan_error = unsafe { record.u.asan() };
    let size = asan_error.uiRuntimeFullMessageLength as usize;
    let remote_message_address = asan_error.pwRuntimeFullMessage as LPCVOID;
    if size == 0 || remote_message_address.is_null() {
        bail!("Empty full message");
    }

    let message = process::read_wide_string(process_handle, remote_message_address, size)?;
    Ok(message.to_string_lossy().to_string())
}

fn get_asan_error_from_runtime_description(message: &str) -> AsanError {
    match message {
        "heap-use-after-free" => AsanError::HeapUseAfterFree,
        "heap-buffer-overflow" => AsanError::HeapBufferOverflow,
        "stack-buffer-underflow" => AsanError::StackBufferUnderflow,
        "initialization-order-fiasco" => AsanError::InitializationOrderFiasco,
        "stack-buffer-overflow" => AsanError::StackBufferOverflow,
        "stack-use-after-return" => AsanError::StackUseAfterReturn,
        "use-after-poison" => AsanError::UseAfterPoison,
        "container-overflow" => AsanError::ContainerOverflow,
        "stack-use-after-scope" => AsanError::StackUseAfterScope,
        "global-buffer-overflow" => AsanError::GlobalBufferOverflow,
        "unknown-crash" => AsanError::UnknownCrash,
        "stack-overflow" => AsanError::StackOverflow,
        "null-deref" => AsanError::NullDeref,
        "wild-jump" => AsanError::WildJump,
        "wild-addr-write" => AsanError::WildAddrWrite,
        "wild-addr-read" => AsanError::WildAddrRead,
        "wild-addr" => AsanError::WildAddr,
        "signal" => AsanError::Signal,
        "double-free" => AsanError::DoubleFree,
        "new-delete-type-mismatch" => AsanError::NewDeleteTypeMismatch,
        "bad-free" => AsanError::BadFree,
        "alloc-dealloc-mismatch" => AsanError::AllocDeallocMismatch,
        "param-overlap" => AsanError::ParamOverlap,
        "negative-size-param" => AsanError::NegativeSizeParam,
        "invalid-pointer-pair" => AsanError::InvalidPointerPair,
        _ => AsanError::UnknownAsanError,
    }
}

pub fn asan_error_from_exception_record(
    process_handle: HANDLE,
    exception_record: &EXCEPTION_RECORD,
) -> AsanError {
    if exception_record.NumberParameters >= 1 {
        let message = get_runtime_description(
            process_handle,
            exception_record.ExceptionInformation[0] as LPCVOID,
        )
        .ok();

        if let Some(message) = message {
            return get_asan_error_from_runtime_description(&message);
        }
    }

    AsanError::UnknownAsanError
}

/// Return the full asan report from the exception record.
pub fn get_asan_report(
    process_handle: HANDLE,
    exception_record: &EXCEPTION_RECORD,
) -> Option<String> {
    if exception_record.NumberParameters >= 1 {
        let message = get_full_message(
            process_handle,
            exception_record.ExceptionInformation[0] as LPCVOID,
        )
        .ok();

        if let Some(message) = message {
            return Some(message);
        }
    }

    None
}
