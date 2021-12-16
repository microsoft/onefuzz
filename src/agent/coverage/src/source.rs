// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(transparent)]
pub struct SourceCoverage {
    pub files: Vec<SourceFileCoverage>,
}

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
pub struct SourceFileCoverage {
    /// UTF-8 encoding of the path to the source file.
    pub file: String,

    pub locations: Vec<SourceCoverageLocation>,
}

#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
pub struct SourceCoverageLocation {
    /// Line number of entry in `file` (1-indexed).
    pub line: u32,

    /// Execution count at location.
    pub count: u32,
}

impl SourceCoverageLocation {
    pub fn new(line: u32, count: u32) -> Result<Self> {
        if line == 0 {
            anyhow::bail!("source lines must be 1-indexed");
        }

        Ok(Self {
            line,
            count,
        })
    }
}

#[cfg(test)]
mod tests {
    use anyhow::Result;
    use serde_json::json;

    use super::*;

    const MAIN_C: &str = "src/bin/main.c";
    const COMMON_C: &str = "src/lib/common.c";

    #[test]
    fn test_source_coverage_location() -> Result<()> {
        let valid = SourceCoverageLocation::new(5, 1)?;
        assert_eq!(
            valid,
            SourceCoverageLocation {
                line: 5,
                count: 1,
            }
        );

        let valid_no_col = SourceCoverageLocation::new(5, 1)?;
        assert_eq!(
            valid_no_col,
            SourceCoverageLocation {
                line: 5,
                count: 1,
            }
        );

        let invalid = SourceCoverageLocation::new(0, 1);
        assert!(invalid.is_err());

        Ok(())
    }

    #[test]
    fn test_source_coverage_full() -> Result<()> {
        let text = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [
                    { "line": 4, "count": 1 },
                    { "line": 9, "count": 0 },
                    { "line": 12, "count": 1 },
                ],
            },
            {
                "file": COMMON_C.to_owned(),
                "locations": [
                    { "line": 5, "count": 0 },
                    { "line": 5, "count": 1 },
                    { "line": 8, "count": 0 },
                ],
            },
        ]))?;

        let coverage = {
            let files = vec![
                SourceFileCoverage {
                    file: MAIN_C.to_owned(),
                    locations: vec![
                        SourceCoverageLocation {
                            line: 4,
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 9,
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 12,
                            count: 1,
                        },
                    ],
                },
                SourceFileCoverage {
                    file: COMMON_C.to_owned(),
                    locations: vec![
                        SourceCoverageLocation {
                            line: 5,
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 5,
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 8,
                            count: 0,
                        },
                    ],
                },
            ];
            SourceCoverage { files }
        };

        let ser = serde_json::to_string(&coverage)?;
        assert_eq!(ser, text);

        let de: SourceCoverage = serde_json::from_str(&text)?;
        assert_eq!(de, coverage);

        Ok(())
    }

    #[test]
    fn test_source_coverage_no_files() -> Result<()> {
        let text = serde_json::to_string(&json!([]))?;

        let coverage = SourceCoverage { files: vec![] };

        let ser = serde_json::to_string(&coverage)?;
        assert_eq!(ser, text);

        let de: SourceCoverage = serde_json::from_str(&text)?;
        assert_eq!(de, coverage);

        Ok(())
    }

    #[test]
    fn test_source_coverage_no_locations() -> Result<()> {
        let text = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [],
            },
            {
                "file": COMMON_C.to_owned(),
                "locations": [],
            },
        ]))?;

        let coverage = {
            let files = vec![
                SourceFileCoverage {
                    file: MAIN_C.to_owned(),
                    locations: vec![],
                },
                SourceFileCoverage {
                    file: COMMON_C.to_owned(),
                    locations: vec![],
                },
            ];
            SourceCoverage { files }
        };

        let ser = serde_json::to_string(&coverage)?;
        assert_eq!(ser, text);

        let de: SourceCoverage = serde_json::from_str(&text)?;
        assert_eq!(de, coverage);

        Ok(())
    }
}
