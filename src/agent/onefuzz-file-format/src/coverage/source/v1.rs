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

#[derive(Default, Deserialize, Serialize)]
pub struct SourceCoverageJson {
    #[serde(flatten)]
    pub files: BTreeMap<SourceFile, FileCoverageJson>,
}

#[derive(Default, Deserialize, Serialize)]
pub struct FileCoverageJson {
    pub lines: BTreeMap<LineNumber, HitCount>,
}

impl From<&SourceCoverage> for SourceCoverageJson {
    fn from(source: &SourceCoverage) -> Self {
        let mut json = SourceCoverageJson::default();

        for (path, file) in &source.files {
            let mut file_json = FileCoverageJson::default();

            for (line, count) in &file.lines {
                let line_number = LineNumber(line.number());
                let hit_count = count.0;
                file_json.lines.insert(line_number, hit_count);
            }

            json.files.insert(path.to_string(), file_json);
        }

        json
    }
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
        let s = format!("{val}");
        serializer.serialize_str(&s)
    }

    pub fn deserialize<'de, D>(deserializer: D) -> Result<u32, D::Error>
    where
        D: Deserializer<'de>,
    {
        let s = String::deserialize(deserializer)?;
        s.parse().map_err(serde::de::Error::custom)
    }
}
