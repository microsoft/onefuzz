// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::ffi::OsString;
use std::path::PathBuf;

pub const PATH: &str = "PATH";
pub const LD_LIBRARY_PATH: &str = "LD_LIBRARY_PATH";

#[allow(clippy::ptr_arg)]
pub fn update_path(path: OsString, to_add: &PathBuf) -> Result<OsString> {
    let mut paths: Vec<_> = std::env::split_paths(&path).collect();
    if !paths.contains(to_add) {
        paths.push(to_add.clone())
    }
    Ok(std::env::join_paths(paths)?)
}

#[allow(clippy::ptr_arg)]
pub fn get_path_with_directory(variable: &str, to_add: &PathBuf) -> Result<OsString> {
    match std::env::var_os(variable) {
        Some(path) => update_path(path, to_add),
        None => Ok(to_add.clone().into()),
    }
}
