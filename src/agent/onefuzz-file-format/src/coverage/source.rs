// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::source::SourceCoverage;

pub mod v0;
pub mod v1;

#[derive(Serialize, Deserialize)]
#[serde(tag = "version", content = "coverage")]
pub enum SourceCoverageJson {
    #[serde(rename = "0.1")]
    V0(v0::SourceCoverageJson),

    #[serde(rename = "1.0")]
    V1(v1::SourceCoverageJson),
}

impl SourceCoverageJson {
    pub fn deserialize(text: &str) -> Result<Self> {
        // Try unversioned legacy format.
        let v0 = serde_json::from_str::<v0::SourceCoverageJson>(text);

        if let Ok(v0) = v0 {
            return Ok(Self::V0(v0));
        }

        // Try versioned formats.
        Ok(serde_json::from_str(text)?)
    }
}

// Convert into the latest format.
impl From<SourceCoverage> for SourceCoverageJson {
    fn from(source: SourceCoverage) -> Self {
        v1::SourceCoverageJson::from(source).into()
    }
}

impl From<v0::SourceCoverageJson> for SourceCoverageJson {
    fn from(v0: v0::SourceCoverageJson) -> Self {
        Self::V0(v0)
    }
}
impl From<v1::SourceCoverageJson> for SourceCoverageJson {
    fn from(v1: v1::SourceCoverageJson) -> Self {
        Self::V1(v1)
    }
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
