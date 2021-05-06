// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#![allow(clippy::as_conversions)]
#![allow(clippy::new_without_default)]

#[cfg(target_os = "windows")]
mod intel;

#[cfg(target_os = "windows")]
pub mod pdb;

#[cfg(target_os = "windows")]
pub mod pe;

pub mod block;
pub mod cache;
pub mod code;
pub mod demangle;
pub mod report;
pub mod sancov;

#[cfg(target_os = "linux")]
pub mod disasm;

pub mod filter;
mod region;

#[cfg(test)]
mod test;
