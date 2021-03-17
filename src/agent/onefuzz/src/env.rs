// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::ffi::OsString;
use std::path::PathBuf;

pub const PATH: &str = "PATH";
pub const LD_LIBRARY_PATH: &str = "LD_LIBRARY_PATH";

pub fn get_path_with_directory(variable: &str, to_add: &PathBuf) -> Result<OsString> {
    match std::env::var_os(variable) {
        Some(path) => {
            let mut paths: Vec<_> = std::env::split_paths(&path).collect();
            if !paths.contains(to_add) {
                paths.push(to_add.clone())
            }
            Ok(std::env::join_paths(paths)?)
        }
        None => Ok(to_add.clone().into()),
    }
}
