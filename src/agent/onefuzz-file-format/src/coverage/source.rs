// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::source::SourceCoverage;

pub mod v0;
pub mod v1;

#[derive(Serialize, Deserialize)]
#[serde(tag = "version")]
pub enum SourceCoverageJson {
    #[serde(rename = "0")]
    V0(v0::SourceCoverageJson),

    #[serde(rename = "1")]
    V1(v1::SourceCoverageJson),
}

impl TryFrom<SourceCoverageJson> for SourceCoverage {
    type Error = anyhow::Error;

    fn try_from(json: SourceCoverageJson) -> Result<Self> {
        use SourceCoverageJson::*;

        match json {
            V0(v0) => v0.try_into(),
            V1(v1) => v1.try_into(),
        }
    }
}
