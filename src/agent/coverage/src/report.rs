// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(transparent)]
pub struct CoverageReport<C, M> {
    pub(crate) entries: Vec<CoverageReportEntry<C, M>>,
}

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
pub struct CoverageReportEntry<C, M> {
    pub(crate) module: String,

    #[serde(flatten)]
    pub(crate) metadata: M,

    #[serde(flatten)]
    pub(crate) coverage: C,
}
