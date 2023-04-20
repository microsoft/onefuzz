// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{ffi::OsString, os::windows::ffi::OsStringExt, path::PathBuf};

use anyhow::Result;
use windows::Win32::{
    Foundation::{HANDLE, MAX_PATH},
    Storage::FileSystem::{GetFinalPathNameByHandleW, FILE_NAME_NORMALIZED},
};

use crate::last_os_error;

pub fn get_path_from_handle(handle: HANDLE) -> Result<PathBuf> {
    let mut actual_len: usize;
    let mut buf: Vec<u16> = vec![0; MAX_PATH as usize];

    loop {
        actual_len = unsafe {
            GetFinalPathNameByHandleW(
                handle,
                &mut buf,
                FILE_NAME_NORMALIZED, // default options - normalized with drive letter
            ) as usize
        };

        if actual_len == 0 {
            return Err(last_os_error());
        }

        if actual_len > buf.len() {
            buf.resize(actual_len, 0);
        } else {
            break;
        }
    }

    debug_assert!(actual_len <= buf.len());
    buf.truncate(actual_len);

    Ok(PathBuf::from(OsString::from_wide(&buf)))
}
