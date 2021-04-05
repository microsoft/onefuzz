// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::upper_case_acronyms)]

use std::ffi::OsStr;

use anyhow::Result;
use log::error;
use winapi::{
    shared::minwindef::{DWORD, TRUE},
    um::werapi::{WerAddExcludedApplication, WerRemoveExcludedApplication},
};
use winreg::{enums::HKEY_LOCAL_MACHINE, RegKey};

use crate::{check_hr, string};

pub fn add_exclusion(exe_name: &OsStr) -> Result<()> {
    let wexe_name = string::to_wstring(&exe_name);
    check_hr!(unsafe {
        WerAddExcludedApplication(wexe_name.as_ptr(), /*AllUsers*/ TRUE)
    });
    atexit::register(move || remove_exclusion(&wexe_name));

    Ok(())
}

fn remove_exclusion(application_path: &[u16]) {
    unsafe {
        // TODO: Minor bug - we shouldn't remove from WER if we weren't the tool to add it.
        WerRemoveExcludedApplication(application_path.as_ptr(), /*AllUsers*/ TRUE);
    }
}

const WINDOWS_ERROR_REPORTING_KEY: &str = r"SOFTWARE\Microsoft\Windows\Windows Error Reporting";
const DONTSHOWUI_PROP: &str = "DontShowUI";

#[derive(Copy, Clone)]
enum RestoreWerUI {
    DeleteKey,
    Value(u32),
}

pub fn disable_wer_ui() -> Result<()> {
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let (wer, _) = hklm.create_subkey(WINDOWS_ERROR_REPORTING_KEY)?;

    let restore = match wer.get_value::<DWORD, _>(DONTSHOWUI_PROP) {
        Err(_) => RestoreWerUI::DeleteKey,
        Ok(v) => RestoreWerUI::Value(v),
    };

    wer.set_value(DONTSHOWUI_PROP, &1u32)?;
    atexit::register(move || restore_wer_ui(restore));

    Ok(())
}

fn restore_wer_ui(restore: RestoreWerUI) {
    if let Err(err) = do_the_work(restore) {
        error!(
            r"Error restoring HKLM:{}\{}: {}",
            WINDOWS_ERROR_REPORTING_KEY, DONTSHOWUI_PROP, err
        );
    }

    fn do_the_work(restore: RestoreWerUI) -> Result<()> {
        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        let (wer, _) = hklm.create_subkey(WINDOWS_ERROR_REPORTING_KEY)?;

        match restore {
            RestoreWerUI::DeleteKey => wer.delete_value(DONTSHOWUI_PROP)?,
            RestoreWerUI::Value(v) => wer.set_value(DONTSHOWUI_PROP, &v)?,
        };

        Ok(())
    }
}
