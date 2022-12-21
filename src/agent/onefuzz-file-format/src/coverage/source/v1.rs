// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;

use anyhow::Result;
use coverage::source::{Count, FileCoverage, Line, SourceCoverage};
use debuggable_module::path::FilePath;
use serde::{Deserialize, Serialize};

pub type SourceFile = String;
pub type HitCount = u32;
pub use line_number::LineNumber;

#[derive(Deserialize, Serialize)]
pub struct SourceCoverageJson {
    #[serde(flatten)]
    pub files: BTreeMap<SourceFile, FileCoverageJson>,
}

#[derive(Deserialize, Serialize)]
pub struct FileCoverageJson {
    pub lines: BTreeMap<LineNumber, HitCount>,
}

impl TryFrom<SourceCoverageJson> for SourceCoverage {
    type Error = anyhow::Error;

    fn try_from(json: SourceCoverageJson) -> Result<Self> {
        let mut source = SourceCoverage::default();

        for (file_path, file_json) in json.files {
            let file_path = FilePath::new(file_path)?;

            let mut file = FileCoverage::default();

            for (line_number, count) in file_json.lines {
                let line = Line::new(line_number.0)?;
                let count = Count(count);
                file.lines.insert(line, count);
            }

            source.files.insert(file_path, file);
        }

        Ok(source)
    }
}

mod line_number {
    use serde::{Deserialize, Deserializer, Serialize, Serializer};

    #[derive(Clone, Copy, Debug, Deserialize, Eq, Ord, PartialEq, PartialOrd, Serialize)]
    pub struct LineNumber(#[serde(with = "self")] pub u32);

    pub fn serialize<S>(val: &u32, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let s = format!("{}", val);
        serializer.serialize_str(&s)
    }

    pub fn deserialize<'de, D>(deserializer: D) -> Result<u32, D::Error>
    where
        D: Deserializer<'de>,
    {
        let s = String::deserialize(deserializer)?;
        u32::from_str_radix(&s, 10).map_err(serde::de::Error::custom)
    }
}
