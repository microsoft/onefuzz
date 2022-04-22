// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::uninit_vec)]

use std::{
    ffi::OsString,
    mem::{size_of, MaybeUninit},
    os::windows::ffi::OsStringExt,
    ptr,
};

use anyhow::{Context, Result};
use log::{error, warn};
use winapi::{
    ctypes::c_void,
    shared::{
        basetsd::SIZE_T,
        minwindef::{BOOL, DWORD, FALSE, LPCVOID, LPVOID, TRUE},
    },
    um::{
        handleapi::{CloseHandle, INVALID_HANDLE_VALUE},
        memoryapi::{ReadProcessMemory, WriteProcessMemory},
        processthreadsapi::{GetCurrentProcess, GetProcessId, OpenProcessToken, TerminateProcess},
        securitybaseapi::GetTokenInformation,
        winnt::{TokenElevation, HANDLE, TOKEN_ELEVATION, TOKEN_QUERY},
        wow64apiset::IsWow64Process,
    },
};

use crate::check_winapi;
use winapi::um::processthreadsapi::FlushInstructionCache;

pub fn is_elevated() -> bool {
    fn is_elevated_impl() -> Result<bool> {
        let mut process_token = INVALID_HANDLE_VALUE;

        check_winapi(|| unsafe {
            OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut process_token)
        })
        .context("Opening process token")?;

        let mut token_elevation: MaybeUninit<TOKEN_ELEVATION> = MaybeUninit::uninit();
        let mut size = size_of::<TOKEN_ELEVATION>() as DWORD;

        check_winapi(|| unsafe {
            GetTokenInformation(
                process_token,
                TokenElevation,
                token_elevation.as_mut_ptr() as *mut c_void,
                size,
                &mut size,
            )
        })
        .context("Getting process token information")?;

        if process_token != INVALID_HANDLE_VALUE {
            unsafe { CloseHandle(process_token) };
        }

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

pub fn read_memory<T: Copy>(process_handle: HANDLE, remote_address: LPCVOID) -> Result<T> {
    let mut buf: MaybeUninit<T> = MaybeUninit::uninit();
    check_winapi(|| unsafe {
        ReadProcessMemory(
            process_handle,
            remote_address,
            buf.as_mut_ptr() as LPVOID,
            size_of::<T>(),
            ptr::null_mut(),
        )
    })
    .context("Reading process memory")?;

    let buf = unsafe { buf.assume_init() };
    Ok(buf)
}

pub fn read_memory_array<T: Copy>(
    process_handle: HANDLE,
    remote_address: LPCVOID,
    buf: &mut [T],
) -> Result<()> {
    check_winapi(|| unsafe {
        ReadProcessMemory(
            process_handle,
            remote_address,
            buf.as_mut_ptr() as LPVOID,
            buf.len() * size_of::<T>(),
            ptr::null_mut(),
        )
    })
    .context("Reading process memory")?;
    Ok(())
}

pub fn read_narrow_string(
    process_handle: HANDLE,
    remote_address: LPCVOID,
    len: usize,
) -> Result<String> {
    let mut buf: Vec<u8> = Vec::with_capacity(len);
    unsafe {
        buf.set_len(len);
    }
    read_memory_array::<u8>(process_handle, remote_address, &mut buf[..])?;
    Ok(String::from_utf8_lossy(&buf).into())
}

pub fn read_wide_string(
    process_handle: HANDLE,
    remote_address: LPCVOID,
    len: usize,
) -> Result<OsString> {
    let mut buf: Vec<u16> = Vec::with_capacity(len);
    unsafe {
        buf.set_len(len);
    }
    read_memory_array::<u16>(process_handle, remote_address, &mut buf[..])?;
    Ok(OsString::from_wide(&buf))
}

pub fn write_memory_slice(
    process_handle: HANDLE,
    remote_address: LPVOID,
    buffer: &[u8],
) -> Result<()> {
    let mut bytes_written: SIZE_T = 0;
    check_winapi(|| unsafe {
        WriteProcessMemory(
            process_handle,
            remote_address,
            buffer.as_ptr() as LPCVOID,
            buffer.len(),
            &mut bytes_written,
        )
    })
    .context("writing process memory")?;

    Ok(())
}

pub fn write_memory<T: Sized>(
    process_handle: HANDLE,
    remote_address: LPVOID,
    value: &T,
) -> Result<()> {
    let mut bytes_written: SIZE_T = 0;
    check_winapi(|| unsafe {
        WriteProcessMemory(
            process_handle,
            remote_address,
            value as *const T as LPCVOID,
            size_of::<T>(),
            &mut bytes_written,
        )
    })
    .context("writing process memory")?;

    Ok(())
}

pub fn id(process_handle: HANDLE) -> DWORD {
    unsafe { GetProcessId(process_handle) }
}

pub fn is_wow64_process(process_handle: HANDLE) -> bool {
    fn is_wow64_process_impl(process_handle: HANDLE) -> Result<bool> {
        let mut is_wow64 = FALSE;
        check_winapi(||
            // If we ever run as a 32 bit process, or if we run on ARM64, this code is wrong,
            // we should be using IsWow64Process2. We don't because it's not supported by
            // every OS we'd like to run on, e.g. the vs2017-win2016 vm we use in CI.
            unsafe { IsWow64Process(process_handle, &mut is_wow64 as *mut BOOL) })
        .context("IsWow64Process")?;
        Ok(is_wow64 == TRUE)
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
        check_winapi(|| unsafe { TerminateProcess(process_handle, 0) })
            .context("TerminateProcess")?;
        Ok(())
    }

    if process_handle != INVALID_HANDLE_VALUE && !process_handle.is_null() {
        if let Err(err) = terminate_impl(process_handle) {
            error!("Error terminating process: {}", err);
        }
    }
}

pub fn flush_instruction_cache(
    process_handle: HANDLE,
    remote_address: LPCVOID,
    len: usize,
) -> Result<()> {
    check_winapi(|| unsafe { FlushInstructionCache(process_handle, remote_address, len) })
}

pub fn current_process_handle() -> HANDLE {
    unsafe { GetCurrentProcess() }
}
