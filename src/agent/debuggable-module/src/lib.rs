// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate log;

use std::fmt;
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

/// Executable code module, with debuginfo.
///
/// The debuginfo may be inline, or split into a separate file.
pub trait Module<'data> {
    /// Path to the executable module file.
    fn executable_path(&self) -> &FilePath;

    /// Path to the file containing debug info for the executable.
    ///
    /// May be the same as the executable path.
    fn debuginfo_path(&self) -> &FilePath;

    /// Read `size` bytes of data from the module-relative virtual offset `offset`.
    ///
    /// Will return an error if the requested region is outside of the range of the
    /// module's image.
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

/// Virtual address.
///
/// May be used to represent an internal fiction of debuginfo, or real address image. In
/// any case, the virtual address is absolute, not module-relative.
///
/// No validity assumption can be made about the address value. For example, it may be:
/// - Zero
/// - In either userspace or kernelspace
/// - Non-canonical for x86-64
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct Address(pub u64);

impl Address {
    /// Returns the address located `offset` bytes above `self`.
    ///
    /// Can be used to convert a module-relative offset to a virtual address by adding it
    /// to a module's base virtual address.
    ///
    /// Fails if the new address is not representable by a `u64`.
    pub fn offset_by(&self, offset: Offset) -> Result<Address> {
        let addr = self
            .0
            .checked_add(offset.0)
            .ok_or_else(|| error!("overflow: {:x} + {:x}", self.0, offset.0))?;

        Ok(Address(addr))
    }

    /// Returns `self` as an `addr`-relative `offset`.
    ///
    /// Can be used to convert a virtual address to a module-relative offset by
    /// subtracting the module's base virtual address.
    ///
    /// Fails if `self` < `addr`.
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

/// Positive byte offset from some byte location.
///
/// May be relative to a virtual address, another virtual offset, or a file position.
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
