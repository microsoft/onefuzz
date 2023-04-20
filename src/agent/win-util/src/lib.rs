// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![cfg(windows)]

mod aedebug;
pub mod file;
pub mod handle;
pub mod memory;
pub mod pipe_handle;
pub mod process;
pub mod string;
mod wer;

use std::path::Path;

use anyhow::Result;
use windows::Win32::System::Diagnostics::Debug::{
    SetErrorMode, SEM_FAILCRITICALERRORS, SEM_NOGPFAULTERRORBOX,
};

pub fn last_os_error() -> anyhow::Error {
    std::io::Error::last_os_error().into()
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
