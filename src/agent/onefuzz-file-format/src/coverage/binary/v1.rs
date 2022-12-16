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
    pub modules: BTreeMap<String, BTreeMap<Hex, u32>>,
}

impl From<BinaryCoverage> for BinaryCoverageJson {
    fn from(binary: BinaryCoverage) -> Self {
        let mut modules = BTreeMap::new();

        for (module, offsets) in &binary.modules {
            let mut map: BTreeMap<Hex, u32> = BTreeMap::new();

            for (offset, count) in offsets.as_ref() {
                map.insert(Hex(offset.0), count.0);
            }

            let path = module.as_str().to_owned();
            modules.insert(path, map);
        }

        Self { modules }
    }
}

impl TryFrom<BinaryCoverageJson> for BinaryCoverage {
    type Error = anyhow::Error;

    fn try_from(json: BinaryCoverageJson) -> Result<Self> {
        let mut process = BinaryCoverage::default();

        for (module, offsets) in json.modules {
            let mut coverage = ModuleBinaryCoverage::default();

            for (hex, count) in offsets {
                let offset = Offset(u64::from(hex.0));
                coverage.offsets.insert(offset, Count(count));
            }

            let path = FilePath::new(module)?;
            process.modules.insert(path, coverage);
        }

        Ok(process)
    }
}
