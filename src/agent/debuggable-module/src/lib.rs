// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate log;

use std::fmt;
use std::io::Cursor;
use std::ops::Range;

use anyhow::{anyhow as error, Result};

pub mod block;
pub mod debuginfo;
pub mod linux;
pub mod load_module;
pub mod loader;
pub mod path;
pub mod windows;

use crate::debuginfo::DebugInfo;
use crate::path::FilePath;

pub trait Module<'data> {
    /// Path to the executable module file.
    fn executable_path(&self) -> &FilePath;

    /// Path to the file containing debug info for the executable.
    ///
    /// May be the same as the executable path.
    fn debuginfo_path(&self) -> &FilePath;

    /// Read `size` bytes of data from the module-relative virtual offset `offset`.
    fn read(&self, offset: Offset, size: u64) -> Result<&'data [u8]>;

    /// Nominal base load address of the module image.
    fn base_address(&self) -> Address;

    /// Raw bytes of the executable file.
    fn executable_data(&self) -> &'data [u8];

    /// Raw bytes of the file that contains debug info.
    ///
    /// May be the same as the executable data.
    fn debuginfo_data(&self) -> &'data [u8];

    /// Debugging information derived from the module and its debuginfo.
    fn debuginfo(&self) -> Result<DebugInfo>;
}

#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct Address(pub u64);

impl Address {
    pub fn offset_by(&self, offset: Offset) -> Result<Address> {
        let addr = self
            .0
            .checked_add(offset.0)
            .ok_or_else(|| error!("overflow: {:x} + {:x}", self.0, offset.0))?;

        Ok(Address(addr))
    }

    pub fn offset_from(&self, addr: Address) -> Result<Offset> {
        let offset = self
            .0
            .checked_sub(addr.0)
            .ok_or_else(|| error!("underflow: {:x} - {:x}", self.0, addr.0))?;

        Ok(Offset(offset))
    }
}

impl fmt::LowerHex for Address {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{:x}", self.0)
    }
}

#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct Offset(pub u64);

impl Offset {
    pub fn region(&self, size: u64) -> Range<u64> {
        let lo = self.0;
        let hi = lo.saturating_add(size);
        lo..hi
    }
}

impl fmt::LowerHex for Offset {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{:x}", self.0)
    }
}

pub fn is_linux_module(data: &[u8]) -> Result<bool> {
    let mut cursor = Cursor::new(data);
    let hint = goblin::peek(&mut cursor)?;
    Ok(matches!(hint, goblin::Hint::Elf(..)))
}

pub fn is_windows_module(data: &[u8]) -> Result<bool> {
    let mut cursor = Cursor::new(data);
    let hint = goblin::peek(&mut cursor)?;
    Ok(matches!(hint, goblin::Hint::PE))
}
