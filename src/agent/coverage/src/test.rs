// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;

use crate::code::ModulePath;

/// Given a POSIX-style path as a string, construct a valid absolute path for
/// the target OS and return it as a checked `ModulePath`.
pub fn module_path(posix_path: &str) -> Result<ModulePath> {
    let mut p = std::path::PathBuf::default();

    // Ensure that the new path is absolute.
    if cfg!(target_os = "windows") {
        p.push("c:\\");
    } else {
        p.push("/");
    }

    // Remove any affixed POSIX path separators, then split on any internal
    // separators and add each component to our accumulator path in an
    // OS-specific way.
    for c in posix_path.trim_matches('/').split('/') {
        p.push(c);
    }

    ModulePath::new(p)
}
