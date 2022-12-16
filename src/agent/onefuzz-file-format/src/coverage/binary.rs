// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::binary::BinaryCoverage;

pub mod v0;
pub mod v1;

#[derive(Serialize, Deserialize)]
#[serde(tag = "version")]
pub enum BinaryCoverageJson {
    #[serde(rename = "0")]
    V0(v0::BinaryCoverageJson),

    #[serde(rename = "1")]
    V1(v1::BinaryCoverageJson),
}

impl TryFrom<BinaryCoverageJson> for BinaryCoverage {
    type Error = anyhow::Error;

    fn try_from(json: BinaryCoverageJson) -> Result<Self> {
        use BinaryCoverageJson::*;

        match json {
            V0(v0) => v0.try_into(),
            V1(v1) => v1.try_into(),
        }
    }
}
