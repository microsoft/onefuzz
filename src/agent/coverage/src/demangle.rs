// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{format_err, Result};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Demangler {
    /// Itanium C++ ABI mangling
    Itanium,

    /// MSVC decorated names
    Msvc,

    /// Rustc name mangling
    Rustc,
}

impl Demangler {
    pub fn demangle(&self, raw: impl AsRef<str>) -> Result<String> {
        let raw = raw.as_ref();

        let demangled = match self {
            Demangler::Itanium => cpp_demangle::Symbol::new(raw)?.to_string(),
            Demangler::Msvc => {
                let flags = msvc_demangler::DemangleFlags::llvm();
                msvc_demangler::demangle(raw, flags)?
            }
            Demangler::Rustc => rustc_demangle::try_demangle(raw)
                .map_err(|_| format_err!("unable to demangle rustc name"))?
                .to_string(),
        };

        Ok(demangled)
    }
}
