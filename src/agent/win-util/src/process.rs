// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    ffi::{c_void, OsString},
    mem::{size_of, size_of_val, MaybeUninit},
    os::windows::ffi::OsStringExt,
};

use anyhow::{Context, Result};
use log::{error, warn};
use windows::Win32::{
    Foundation::{CloseHandle, FALSE, HANDLE, INVALID_HANDLE_VALUE},
    Security::{GetTokenInformation, TokenElevation, TOKEN_ELEVATION, TOKEN_QUERY},
    System::{
        Diagnostics::Debug::{FlushInstructionCache, ReadProcessMemory, WriteProcessMemory},
        Threading::{
            GetCurrentProcess, GetProcessId, IsWow64Process, OpenProcessToken, TerminateProcess,
        },
    },
};

pub fn is_elevated() -> bool {
    fn is_elevated_impl() -> Result<bool> {
        let mut process_token = INVALID_HANDLE_VALUE;

        unsafe { OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut process_token) }
            .ok()
            .context("Opening process token")?;

        let mut token_elevation: MaybeUninit<TOKEN_ELEVATION> = MaybeUninit::uninit();
        let mut size = size_of::<TOKEN_ELEVATION>() as u32;

        unsafe {
            GetTokenInformation(
                process_token,
                TokenElevation,
                Some(token_elevation.as_mut_ptr().cast()),
                size,
                &mut size,
            )
        }
        .ok()
        .context("Getting process token information")?;

        if process_token != INVALID_HANDLE_VALUE {
            unsafe { CloseHandle(process_token) };
        }

        debug_assert!(size == size_of::<TOKEN_ELEVATION>() as u32);

        let token_elevation = unsafe { token_elevation.assume_init() };
        Ok(token_elevation.TokenIsElevated != 0)
    }

    match is_elevated_impl() {
        Ok(elevated) => elevated,
        Err(err) => {
            warn!("Error checking if process is elevated: {}", err);
            false
        }
    }
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn read_memory<T: Copy>(process_handle: HANDLE, remote_address: *const c_void) -> Result<T> {
    let mut buf: MaybeUninit<T> = MaybeUninit::uninit();
    unsafe {
        ReadProcessMemory(
            process_handle,
            remote_address,
            buf.as_mut_ptr().cast(),
            size_of::<T>(),
            None,
        )
    }
    .ok()
    .context("Reading process memory")?;

    let buf = unsafe { buf.assume_init() };
    Ok(buf)
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn read_memory_array<T: Copy>(
    process_handle: HANDLE,
    remote_address: *const c_void,
    buf: &mut [T],
) -> Result<()> {
    unsafe {
        ReadProcessMemory(
            process_handle,
            remote_address,
            buf.as_mut_ptr().cast(),
            size_of_val(buf),
            None,
        )
    }
    .ok()
    .context("Reading process memory")?;

    Ok(())
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn read_narrow_string(
    process_handle: HANDLE,
    remote_address: *const c_void,
    len: usize,
) -> Result<String> {
    let mut buf: Vec<u8> = Vec::with_capacity(len);
    read_memory_array(process_handle, remote_address, buf.spare_capacity_mut())?;
    unsafe {
        buf.set_len(len);
    }
    Ok(String::from_utf8_lossy(&buf).into())
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn read_wide_string(
    process_handle: HANDLE,
    remote_address: *const c_void,
    len: usize,
) -> Result<OsString> {
    let mut buf: Vec<u16> = Vec::with_capacity(len);
    read_memory_array(process_handle, remote_address, buf.spare_capacity_mut())?;
    unsafe {
        buf.set_len(len);
    }
    Ok(OsString::from_wide(&buf))
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn write_memory_slice(
    process_handle: HANDLE,
    remote_address: *mut c_void,
    buffer: &[u8],
) -> Result<()> {
    let mut bytes_written: usize = 0;
    unsafe {
        WriteProcessMemory(
            process_handle,
            remote_address,
            buffer.as_ptr().cast(),
            buffer.len(),
            Some(&mut bytes_written),
        )
    }
    .ok()
    .context("writing process memory")?;

    Ok(())
}

#[allow(clippy::not_unsafe_ptr_arg_deref)] // pointer is not in this process
pub fn write_memory<T: Sized>(
    process_handle: HANDLE,
    remote_address: *mut c_void,
    value: &T,
) -> Result<()> {
    let mut bytes_written: usize = 0;
    unsafe {
        WriteProcessMemory(
            process_handle,
            remote_address,
            (value as *const T).cast(),
            size_of::<T>(),
            Some(&mut bytes_written),
        )
    }
    .ok()
    .context("writing process memory")?;

    Ok(())
}

pub fn id(process_handle: HANDLE) -> u32 {
    unsafe { GetProcessId(process_handle) }
}

pub fn is_wow64_process(process_handle: HANDLE) -> bool {
    #[cfg(target_arch = "x86_64")] // break build on ARM64
    fn is_wow64_process_impl(process_handle: HANDLE) -> Result<bool> {
        let mut is_wow64 = FALSE;
        // If we ever run as a 32 bit process, or if we run on ARM64, this code is wrong,
        // we should be using IsWow64Process2. We don't because it's not supported by
        // every OS we'd like to run on, e.g. the vs2017-win2016 vm we use in CI.
        unsafe { IsWow64Process(process_handle, &mut is_wow64) }
            .ok()
            .context("IsWow64Process")?;

        Ok(is_wow64.as_bool())
    }

    match is_wow64_process_impl(process_handle) {
        Ok(result) => result,
        Err(err) => {
            warn!("Error checking if process is wow64: {}", err);
            false
        }
    }
}

pub fn terminate(process_handle: HANDLE) {
    fn terminate_impl(process_handle: HANDLE) -> Result<()> {
        unsafe { TerminateProcess(process_handle, 0) }
            .ok()
            .context("TerminateProcess")
    }

    if process_handle != INVALID_HANDLE_VALUE && process_handle != HANDLE::default() {
        if let Err(err) = terminate_impl(process_handle) {
            error!("Error terminating process: {}", err);
        }
    }
}

pub fn flush_instruction_cache(
    process_handle: HANDLE,
    remote_address: *const c_void,
    len: usize,
) -> Result<()> {
    unsafe { FlushInstructionCache(process_handle, Some(remote_address), len) }.ok()?;
    Ok(())
}
