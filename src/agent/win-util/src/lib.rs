// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![cfg(windows)]
// Allow safe functions that take `HANDLE` arguments.
//
// Though they type alias raw pointers, they are opaque. In the future, we will
// wrap them in a newtype. This will witness that they were obtained via win32
// API calls or documented pseudohandle construction.
#![allow(clippy::not_unsafe_ptr_arg_deref)]

#[macro_use]
pub mod macros;

#[macro_use]
pub mod hr;

mod aedebug;
pub mod com;
pub mod file;
pub mod handle;
pub mod jobs;
pub mod memory;
pub mod pipe_handle;
pub mod process;
pub mod string;
mod wer;

use std::path::Path;

use anyhow::Result;
use winapi::{
    shared::minwindef::{BOOL, FALSE},
    um::{
        errhandlingapi::SetErrorMode,
        winbase::{SEM_FAILCRITICALERRORS, SEM_NOGPFAULTERRORBOX},
    },
};

pub fn last_os_error() -> anyhow::Error {
    std::io::Error::last_os_error().into()
}

/// Call a windows api that returns BOOL, and if it fails, returns the os error
pub fn check_winapi<T: FnOnce() -> BOOL>(f: T) -> Result<()> {
    if f() == FALSE {
        Err(last_os_error())
    } else {
        Ok(())
    }
}

pub fn configure_machine_wide_app_debug_settings(application_path: impl AsRef<Path>) -> Result<()> {
    anyhow::ensure!(
        process::is_elevated(),
        "Changing registry requires elevation"
    );

    let exe_name = application_path.as_ref().file_name().ok_or_else(|| {
        anyhow::anyhow!(
            "Missing executable name in path {}",
            application_path.as_ref().display()
        )
    })?;

    // This should avoid some popups, e.g. if a dll can't be found.
    // I'm not sure SEM_NOGPFAULTERRORBOX is useful anymore (because of Watson),
    // but it is another source of popups that could block automation.
    unsafe { SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX) };

    // This is a machine-wide setting, not process specific.
    wer::disable_wer_ui()?;

    wer::add_exclusion(exe_name)?;
    aedebug::add_exclusion(exe_name)?;

    Ok(())
}
