// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::upper_case_acronyms)]

//! A module used to wrap an HRESULT for use as an Error.
use std::fmt::{self, Display, Formatter};

use winapi::shared::winerror::HRESULT;

#[derive(Copy, Clone, Debug)]
pub struct HRESULTError(pub HRESULT);

impl Display for HRESULTError {
    fn fmt(&self, formatter: &mut Formatter) -> Result<(), fmt::Error> {
        write!(formatter, "HRESULT: {:x}", self.0)
    }
}

impl std::error::Error for HRESULTError {}

#[macro_export]
macro_rules! check_hr {
    ($x:expr) => {
        let hr = $x;
        if !winapi::shared::winerror::SUCCEEDED(hr) {
            return Err(From::from($crate::hr::HRESULTError(hr)));
        }
    };
}
