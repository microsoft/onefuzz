// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(any(target_os = "linux", target_os = "windows"))]
pub mod generic;
#[cfg(any(target_os = "linux", target_os = "windows"))]
pub mod libfuzzer_coverage;
#[cfg(any(target_os = "linux", target_os = "windows"))]
pub mod recorder;
pub mod total;
