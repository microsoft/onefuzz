// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use serde::{Deserialize, Serialize};

/// Generic container for a code coverage report.
///
/// Coverage is reported as a sequence of module coverage entries, which are
/// generic in a coverage type `C` and a metadata type `M`.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(transparent)]
pub struct CoverageReport<C, M> {
    /// Coverage data for each module.
    pub(crate) entries: Vec<CoverageReportEntry<C, M>>,
}

/// A generic entry in a code coverage report.
///
/// `C` is the coverage type. It should have a field whose value is a map or
/// sequence of instrumented sites and associated counters.
///
/// `M` is the metadata type. It should include additional data about the module
/// itself. It enables tracking provenance and disambiguating modules when the
/// `module` field is insufficient. Examples: module file checksums, process
/// identifiers. If not desired, it may be set to `()`.
///
/// The types `C` and `M` must be structs with named fields, and should at least
/// implement `Serialize` and `Deserialize`.
///
/// Warning: `serde` allows duplicate keys. If `M` and `C` share field names as
/// structs, then the serialized entry will have duplicate keys.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
pub struct CoverageReportEntry<C, M> {
    /// Path or name of the module.
    pub(crate) module: String,

    /// Metadata to identify or contextualize the module.
    #[serde(flatten)]
    pub(crate) metadata: M,

    /// Coverage data for the module.
    #[serde(flatten)]
    pub(crate) coverage: C,
}
