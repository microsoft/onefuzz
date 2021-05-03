// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use serde::{Deserialize, Serialize};

/// Generic container for a code coverage report.
///
/// Coverage is reported as a sequence of module coverage entries, which are
/// generic in a coverage type `C` and a metadata type `M`.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(transparent)]
pub struct CoverageReport<C, M> {
    /// Coverage data for each module.
    pub entries: Vec<CoverageReportEntry<C, M>>,
}

/// A generic entry in a code coverage report.
///
/// `C` is the coverage type. It should have a field whose value is a map or
/// sequence of instrumented sites and associated counters.
///
/// `M` is the metadata type. It should include additional data about the module
/// itself. It enables tracking provenance and disambiguating modules when the
/// `module` field is insufficient. Examples: module file checksums, process
/// identifiers. If not desired, it may be set to `()`.
///
/// The types `C` and `M` must be structs with named fields, and should at least
/// implement `Serialize` and `Deserialize`.
///
/// Warning: `serde` allows duplicate keys. If `M` and `C` share field names as
/// structs, then the serialized entry will have duplicate keys.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
pub struct CoverageReportEntry<C, M> {
    /// Path or name of the module.
    pub module: String,

    /// Metadata to identify or contextualize the module.
    #[serde(flatten)]
    pub metadata: M,

    /// Coverage data for the module.
    #[serde(flatten)]
    pub coverage: C,
}

#[cfg(test)]
mod tests {
    use anyhow::Result;
    use serde_json::json;

    use crate::test::module_path;

    use super::*;

    #[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
    struct Metadata {
        checksum: String,
        pid: u64,
    }

    #[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
    struct Edge {
        edges: Vec<EdgeCov>,
    }

    #[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
    struct EdgeCov {
        src: u32,
        dst: u32,
        count: u32,
    }

    // Example of using `CoverageReport` for alternative coverage types.
    type EdgeCoverageReport = CoverageReport<Edge, Metadata>;

    #[test]
    fn test_coverage_report() -> Result<()> {
        let main_exe = module_path("/onefuzz/main.exe")?;
        let some_dll = module_path("/common/some.dll")?;

        let text = serde_json::to_string(&json!([
            {
                "module": some_dll,
                "checksum": "5feceb66",
                "pid": 123,
                "edges": [
                    { "src": 10, "dst": 20, "count": 0 },
                    { "src": 10, "dst": 30, "count": 1 },
                    { "src": 30, "dst": 40, "count": 1 },
                ],
            },
            {
                "module": some_dll,
                "checksum": "ffc86f38",
                "pid": 456,
                "edges": [
                    { "src": 100, "dst": 200, "count": 1 },
                    { "src": 200, "dst": 300, "count": 0 },
                    { "src": 300, "dst": 400, "count": 0 },
                ],
            },
            {
                "module": main_exe,
                "checksum": "d952786c",
                "pid": 123,
                "edges": [
                    { "src": 1000, "dst": 2000, "count": 1 },
                    { "src": 2000, "dst": 3000, "count": 0 },
                ],
            },
        ]))?;

        let report = EdgeCoverageReport {
            entries: vec![
                CoverageReportEntry {
                    module: some_dll.to_string(),
                    metadata: Metadata {
                        checksum: "5feceb66".into(),
                        pid: 123,
                    },
                    coverage: Edge {
                        edges: vec![
                            EdgeCov {
                                src: 10,
                                dst: 20,
                                count: 0,
                            },
                            EdgeCov {
                                src: 10,
                                dst: 30,
                                count: 1,
                            },
                            EdgeCov {
                                src: 30,
                                dst: 40,
                                count: 1,
                            },
                        ],
                    },
                },
                CoverageReportEntry {
                    module: some_dll.to_string(),
                    metadata: Metadata {
                        checksum: "ffc86f38".into(),
                        pid: 456,
                    },
                    coverage: Edge {
                        edges: vec![
                            EdgeCov {
                                src: 100,
                                dst: 200,
                                count: 1,
                            },
                            EdgeCov {
                                src: 200,
                                dst: 300,
                                count: 0,
                            },
                            EdgeCov {
                                src: 300,
                                dst: 400,
                                count: 0,
                            },
                        ],
                    },
                },
                CoverageReportEntry {
                    module: main_exe.to_string(),
                    metadata: Metadata {
                        checksum: "d952786c".into(),
                        pid: 123,
                    },
                    coverage: Edge {
                        edges: vec![
                            EdgeCov {
                                src: 1000,
                                dst: 2000,
                                count: 1,
                            },
                            EdgeCov {
                                src: 2000,
                                dst: 3000,
                                count: 0,
                            },
                        ],
                    },
                },
            ],
        };

        let ser = serde_json::to_string(&report)?;
        assert_eq!(ser, text);

        let de: EdgeCoverageReport = serde_json::from_str(&text)?;
        assert_eq!(de, report);

        Ok(())
    }
}
