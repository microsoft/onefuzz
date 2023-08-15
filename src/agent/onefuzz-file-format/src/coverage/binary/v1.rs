// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;

use anyhow::Result;
use coverage::binary::{BinaryCoverage, Count, ModuleBinaryCoverage};
use debuggable_module::path::FilePath;
use debuggable_module::Offset;

use crate::hex::Hex;

#[derive(Deserialize, Serialize)]
pub struct BinaryCoverageJson {
    #[serde(flatten)]
    pub modules: BTreeMap<String, ModuleCoverageJson>,
}

#[derive(Deserialize, Serialize)]
pub struct ModuleCoverageJson {
    pub blocks: BTreeMap<Hex, u32>,
}

impl From<&BinaryCoverage> for BinaryCoverageJson {
    fn from(binary: &BinaryCoverage) -> Self {
        let mut modules = BTreeMap::new();

        for (path, offsets) in &binary.modules {
            let mut blocks: BTreeMap<Hex, u32> = BTreeMap::new();

            for (offset, count) in offsets.as_ref() {
                blocks.insert(Hex(offset.0), count.0);
            }

            let path = path.as_str().to_owned();
            let module = ModuleCoverageJson { blocks };

            modules.insert(path, module);
        }

        Self { modules }
    }
}

impl TryFrom<BinaryCoverageJson> for BinaryCoverage {
    type Error = anyhow::Error;

    fn try_from(json: BinaryCoverageJson) -> Result<Self> {
        let mut process = BinaryCoverage::default();

        for (path, module) in json.modules {
            let mut coverage = ModuleBinaryCoverage::default();

            for (hex, count) in module.blocks {
                let offset = Offset(hex.0);
                coverage.offsets.insert(offset, Count(count));
            }

            let path = FilePath::new(path)?;
            process.modules.insert(path, coverage);
        }

        Ok(process)
    }
}
