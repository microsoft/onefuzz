// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::ffi::{OsStr, OsString};

use anyhow::{Context, Result};
use log::error;
use winreg::{
    enums::{HKEY_LOCAL_MACHINE, KEY_SET_VALUE, KEY_WOW64_32KEY},
    RegKey,
};

const AEDEBUG_EXCLUSION_LIST: &str =
    r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AeDebug\AutoExclusionList";

pub fn add_exclusion(exe_name: &OsStr) -> Result<()> {
    edit_exclusion_list(|key| key.set_value(&exe_name, &1u32))?;
    let exe_name = exe_name.to_owned();
    atexit::register(move || remove_exclusion(&exe_name));
    Ok(())
}

fn edit_exclusion_list<F: Fn(RegKey) -> ::core::result::Result<(), std::io::Error>>(
    f: F,
) -> Result<()> {
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);

    // We want to set both the 32 and 64 bit registries.
    for flags in [0, KEY_WOW64_32KEY] {
        let exclusion_list = hklm
            .open_subkey_with_flags(AEDEBUG_EXCLUSION_LIST, KEY_SET_VALUE | flags)
            .context("Opening AeDebug\\AutoExclusionList")?;
        f(exclusion_list)?;
    }

    Ok(())
}

fn remove_exclusion(exe_name: &OsString) {
    if let Err(err) = edit_exclusion_list(|key| key.delete_value(&exe_name)) {
        error!("Error removing aedebug exclusions: {}", err);
    }
}
