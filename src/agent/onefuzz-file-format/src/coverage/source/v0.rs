// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::source::{Count, FileCoverage, Line, SourceCoverage};
use debuggable_module::path::FilePath;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Eq, Serialize)]
#[serde(transparent)]
pub struct SourceCoverageJson {
    pub files: Vec<SourceFile>,
}

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Eq, Serialize)]
pub struct SourceFile {
    /// UTF-8 encoding of the path to the source file.
    pub file: String,

    pub locations: Vec<Location>,
}

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Eq, Serialize)]
pub struct Location {
    /// Line number of entry in `file` (1-indexed).
    pub line: u32,

    /// Optional column offset (0-indexed).
    ///
    /// When column offsets are present, they should be interpreted as the start
    /// of a span bounded by the next in-line column offset (or end-of-line).
    pub column: Option<u32>,

    /// Execution count at location.
    pub count: u32,
}

impl TryFrom<SourceCoverageJson> for SourceCoverage {
    type Error = anyhow::Error;

    fn try_from(json: SourceCoverageJson) -> Result<Self> {
        let mut source = SourceCoverage::default();

        for file in json.files {
            let file_path = FilePath::new(&file.file)?;
            let mut file_coverage = FileCoverage::default();

            for location in file.locations {
                let line = Line::new(location.line)?;
                let count = Count(location.count);
                file_coverage.lines.insert(line, count);
            }

            source.files.insert(file_path, file_coverage);
        }

        Ok(source)
    }
}
