// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::binary::{BinaryCoverage, Count, ModuleBinaryCoverage};
use debuggable_module::path::FilePath;
use debuggable_module::Offset;
use serde::{Deserialize, Serialize};

#[derive(Deserialize, Serialize)]
#[serde(transparent)]
pub struct BinaryCoverageJson {
    #[serde(flatten)]
    pub modules: Vec<BinaryModuleCoverageJson>,
}

#[derive(Deserialize, Serialize)]
pub struct BinaryModuleCoverageJson {
    pub module: String,
    pub blocks: Vec<BinaryBlockCoverageJson>,
}

#[derive(Deserialize, Serialize)]
pub struct BinaryBlockCoverageJson {
    pub offset: u32,
    pub count: u32,
}

impl TryFrom<BinaryCoverage> for BinaryCoverageJson {
    type Error = anyhow::Error;

    fn try_from(binary: BinaryCoverage) -> Result<Self> {
        let mut modules = Vec::new();

        for (module, offsets) in binary.modules {
            let mut blocks = Vec::new();

            for (offset, count) in offsets.as_ref() {
                let offset = u32::try_from(offset.0)?;
                let count = count.0;
                let block = BinaryBlockCoverageJson { offset, count };
                blocks.push(block);
            }

            let module = module.as_str().to_owned();
            let module = BinaryModuleCoverageJson { module, blocks };

            modules.push(module);
        }

        Ok(Self { modules })
    }
}

impl TryFrom<BinaryCoverageJson> for BinaryCoverage {
    type Error = anyhow::Error;

    fn try_from(json: BinaryCoverageJson) -> Result<Self> {
        let mut process = BinaryCoverage::default();

        for coverage_json in json.modules {
            let mut coverage = ModuleBinaryCoverage::default();

            for block in coverage_json.blocks {
                let offset = Offset(u64::from(block.offset));
                let count = Count(block.count);
                coverage.offsets.insert(offset, count);
            }

            let path = FilePath::new(coverage_json.module)?;

            process.modules.insert(path, coverage);
        }

        Ok(process)
    }
}

#[cfg(test)]
mod tests;
