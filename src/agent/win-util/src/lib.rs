// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![cfg(windows)]

pub mod file;
pub mod handle;
pub mod memory;
pub mod pipe_handle;
pub mod process;
pub mod string;

pub fn last_os_error() -> anyhow::Error {
    std::io::Error::last_os_error().into()
}
