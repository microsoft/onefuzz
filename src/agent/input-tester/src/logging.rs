// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fmt::Write;
use std::{ffi::OsStr, path::Path, process::Output};

use log::warn;

pub fn command_invocation<S, T, I>(command: S, args: I) -> String
where
    S: AsRef<OsStr>,
    T: AsRef<OsStr>,
    I: IntoIterator<Item = T>,
{
    let mut result = command.as_ref().to_string_lossy().to_string();

    for arg in args {
        result.push(' ');
        let needs_quotes = arg.as_ref().to_string_lossy().find(' ').is_some();
        if needs_quotes {
            result.push('"');
        }
        let arg: &Path = arg.as_ref().as_ref();
        if let Err(e) = write!(result, "{}", arg.display()) {
            warn!("Failed to write arg: {} with error: {}", arg.display(), e);
        }
        if needs_quotes {
            result.push('"');
        }
    }

    result
}

#[derive(Debug)]
#[allow(dead_code)]
pub struct ProcessDetails<'a> {
    pid: u32,
    output: &'a Output,
}

impl<'a> ProcessDetails<'a> {
    pub fn new(pid: u32, output: &'a Output) -> Self {
        ProcessDetails { pid, output }
    }
}
