// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{ffi::OsString, os::windows::ffi::OsStringExt, path::PathBuf};

use anyhow::Result;
use winapi::{
    shared::minwindef::{DWORD, MAX_PATH},
    um::{fileapi::GetFinalPathNameByHandleW, winnt::HANDLE},
};

use crate::last_os_error;

pub fn get_path_from_handle(handle: HANDLE) -> Result<PathBuf> {
    let mut actual_len: usize;
    let mut buf: Vec<u16> = Vec::with_capacity(MAX_PATH);

    loop {
        actual_len = unsafe {
            GetFinalPathNameByHandleW(
                handle,
                buf.as_mut_ptr(),
                buf.capacity() as DWORD,
                0, // default options - normalized with drive letter
            ) as usize
        };

        if actual_len == 0 {
            return Err(last_os_error());
        }

        if actual_len > buf.capacity() {
            buf.reserve(actual_len);
        } else {
            break;
        }
    }

    unsafe {
        buf.set_len(actual_len);
    }

    Ok(PathBuf::from(OsString::from_wide(&buf)))
}
