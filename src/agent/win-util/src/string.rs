// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    ffi::{OsStr, OsString},
    iter::once,
    os::windows::ffi::{OsStrExt, OsStringExt},
    path::Path,
    slice,
};

use windows::{
    core::{Error, PCWSTR},
    Win32::{Foundation::HLOCAL, System::Memory::LocalFree, UI::Shell::CommandLineToArgvW},
};

pub fn to_wstring(str: impl AsRef<Path>) -> Vec<u16> {
    OsStr::new(str.as_ref())
        .encode_wide()
        .chain(once(0))
        .collect()
}

pub fn to_argv(command_line: &str) -> Vec<OsString> {
    let mut argv: Vec<OsString> = Vec::new();
    let mut argc = 0;
    let wide_command_line = to_wstring(command_line);
    unsafe {
        let args = CommandLineToArgvW(PCWSTR(wide_command_line.as_ptr()), &mut argc);
        assert!(!args.is_null());

        for i in 0..argc {
            argv.push(os_string_from_wide_ptr((*args.offset(i as isize)).0));
        }

        let free_result = LocalFree(HLOCAL(args as _));
        // the return value here is backwards from what you'd expect
        // "The operation completed successfully."
        debug_assert!(free_result == Err(Error::OK));
    }
    argv
}

/// # Safety
pub unsafe fn os_string_from_wide_ptr(ptr: *const u16) -> OsString {
    let mut len = 0;
    while *ptr.offset(len) != 0 {
        len += 1;
    }

    // Push it onto the list.
    let ptr = ptr as *const u16;
    let buf = slice::from_raw_parts(ptr, len as usize);
    OsStringExt::from_wide(buf)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn to_argv_simple() {
        let argv = to_argv("a bb");
        assert_eq!(2, argv.len());
        assert_eq!("a", argv[0]);
        assert_eq!("bb", argv[1]);
    }

    #[test]
    fn to_argv_with_quotes() {
        let argv = to_argv("a \"b b\"");
        assert_eq!(2, argv.len());
        assert_eq!("a", argv[0]);
        assert_eq!("b b", argv[1]);
    }
}
