// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate log;

pub mod allowlist;
pub mod binary;
pub mod cobertura;
pub mod record;
pub mod source;
mod timer;

#[doc(inline)]
pub use allowlist::{AllowList, TargetAllowList};

#[doc(inline)]
pub use record::{CoverageRecorder, Recorded};
