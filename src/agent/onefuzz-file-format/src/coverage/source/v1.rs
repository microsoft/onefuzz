// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;

use anyhow::Result;
use coverage::source::{Count, FileCoverage, Line, SourceCoverage};
use debuggable_module::path::FilePath;
use serde::{Deserialize, Serialize};

pub type SourceFile = String;
pub type LineNumber = u32;
pub type HitCount = u32;

#[derive(Deserialize, Serialize)]
pub struct SourceCoverageJson {
    #[serde(flatten)]
    pub modules: BTreeMap<SourceFile, BTreeMap<LineNumber, HitCount>>,
}

impl TryFrom<SourceCoverageJson> for SourceCoverage {
    type Error = anyhow::Error;

    fn try_from(json: SourceCoverageJson) -> Result<Self> {
        let mut source = SourceCoverage::default();

        for (file_path, lines) in json.modules {
            let file_path = FilePath::new(file_path)?;

            let mut file = FileCoverage::default();

            for (line, count) in lines {
                let line = Line::new(line)?;
                let count = Count(count);
                file.lines.insert(line, count);
            }

            source.files.insert(file_path, file);
        }

        Ok(source)
    }
}
