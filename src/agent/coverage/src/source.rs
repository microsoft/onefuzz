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

    /// Optional column offset (0-indexed).
    ///
    /// When column offsets are present, they should be interpreted as the start
    /// of a span bounded by the next in-line column offset (or end-of-line).
    pub column: Option<u32>,

    /// Execution count at location.
    pub count: u32,
}

impl SourceCoverageLocation {
    pub fn new(line: u32, column: impl Into<Option<u32>>, count: u32) -> Result<Self> {
        if line == 0 {
            anyhow::bail!("source lines must be 1-indexed");
        }

        let column = column.into();

        Ok(Self {
            line,
            column,
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
        let valid = SourceCoverageLocation::new(5, 4, 1)?;
        assert_eq!(
            valid,
            SourceCoverageLocation {
                line: 5,
                column: Some(4),
                count: 1,
            }
        );

        let valid_no_col = SourceCoverageLocation::new(5, None, 1)?;
        assert_eq!(
            valid_no_col,
            SourceCoverageLocation {
                line: 5,
                column: None,
                count: 1,
            }
        );

        let invalid = SourceCoverageLocation::new(0, 4, 1);
        assert!(invalid.is_err());

        Ok(())
    }

    #[test]
    fn test_source_coverage_full() -> Result<()> {
        let text = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [
                    { "line": 4, "column": 4, "count": 1 },
                    { "line": 9, "column": 4, "count": 0 },
                    { "line": 12, "column": 4, "count": 1 },
                ],
            },
            {
                "file": COMMON_C.to_owned(),
                "locations": [
                    { "line": 5, "column": 4, "count": 0 },
                    { "line": 5, "column": 9, "count": 1 },
                    { "line": 8, "column": 0, "count": 0 },
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
                            column: Some(4),
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 9,
                            column: Some(4),
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 12,
                            column: Some(4),
                            count: 1,
                        },
                    ],
                },
                SourceFileCoverage {
                    file: COMMON_C.to_owned(),
                    locations: vec![
                        SourceCoverageLocation {
                            line: 5,
                            column: Some(4),
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 5,
                            column: Some(9),
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 8,
                            column: Some(0),
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

    #[test]
    fn test_source_coverage_no_or_null_columns() -> Result<()> {
        let text_null_cols = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [
                    { "line": 4, "column": null, "count": 1 },
                    { "line": 9, "column": null, "count": 0 },
                    { "line": 12, "column": null, "count": 1 },
                ],
            },
            {
                "file": COMMON_C.to_owned(),
                "locations": [
                    { "line": 5, "column": null, "count": 0 },
                    { "line": 5, "column": null, "count": 1 },
                    { "line": 8, "column": null, "count": 0 },
                ],
            },
        ]))?;

        let text_no_cols = serde_json::to_string(&json!([
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
                            column: None,
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 9,
                            column: None,
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 12,
                            column: None,
                            count: 1,
                        },
                    ],
                },
                SourceFileCoverage {
                    file: COMMON_C.to_owned(),
                    locations: vec![
                        SourceCoverageLocation {
                            line: 5,
                            column: None,
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 5,
                            column: None,
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 8,
                            column: None,
                            count: 0,
                        },
                    ],
                },
            ];
            SourceCoverage { files }
        };

        // Serialized with present `column` keys, `null` values.
        let ser = serde_json::to_string(&coverage)?;
        assert_eq!(ser, text_null_cols);

        // Deserializes when `column` keys are absent.
        let de_no_cols: SourceCoverage = serde_json::from_str(&text_no_cols)?;
        assert_eq!(de_no_cols, coverage);

        // Deserializes when `column` keys are present but `null`.
        let de_null_cols: SourceCoverage = serde_json::from_str(&text_null_cols)?;
        assert_eq!(de_null_cols, coverage);

        Ok(())
    }

    #[test]
    fn test_source_coverage_partial_columns() -> Result<()> {
        let text = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [
                    { "line": 4, "column": 4, "count": 1 },
                    { "line": 9, "column": 4, "count": 0 },
                    { "line": 12, "column": 4, "count": 1 },
                ],
            },
            {
                "file": COMMON_C.to_owned(),
                "locations": [
                    { "line": 5, "column": null, "count": 0 },
                    { "line": 5, "column": null, "count": 1 },
                    { "line": 8, "column": null, "count": 0 },
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
                            column: Some(4),
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 9,
                            column: Some(4),
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 12,
                            column: Some(4),
                            count: 1,
                        },
                    ],
                },
                SourceFileCoverage {
                    file: COMMON_C.to_owned(),
                    locations: vec![
                        SourceCoverageLocation {
                            line: 5,
                            column: None,
                            count: 0,
                        },
                        SourceCoverageLocation {
                            line: 5,
                            column: None,
                            count: 1,
                        },
                        SourceCoverageLocation {
                            line: 8,
                            column: None,
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
    fn test_source_coverage_mixed_columns() -> Result<()> {
        let text = serde_json::to_string(&json!([
            {
                "file": MAIN_C.to_owned(),
                "locations": [
                    { "line": 4, "column": null, "count": 1 },
                    { "line": 9, "column": 4, "count": 0 },
                    { "line": 12, "column": null, "count": 1 },
                    { "line": 13, "column": 7, "count": 0 },
                ],
            },
        ]))?;

        let coverage = {
            let files = vec![SourceFileCoverage {
                file: MAIN_C.to_owned(),
                locations: vec![
                    SourceCoverageLocation {
                        line: 4,
                        column: None,
                        count: 1,
                    },
                    SourceCoverageLocation {
                        line: 9,
                        column: Some(4),
                        count: 0,
                    },
                    SourceCoverageLocation {
                        line: 12,
                        column: None,
                        count: 1,
                    },
                    SourceCoverageLocation {
                        line: 13,
                        column: Some(7),
                        count: 0,
                    },
                ],
            }];
            SourceCoverage { files }
        };

        let ser = serde_json::to_string(&coverage)?;
        assert_eq!(ser, text);

        let de: SourceCoverage = serde_json::from_str(&text)?;
        assert_eq!(de, coverage);

        Ok(())
    }
}
