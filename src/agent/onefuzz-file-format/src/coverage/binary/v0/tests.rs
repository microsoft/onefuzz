// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use pretty_assertions::assert_eq;
use serde_json::json;

use super::*;

const MAIN_EXE: &str = "/setup/main.exe";
const SOME_DLL: &str = "/setup/lib/some.dll";

const EXPECTED: &str = r#"
[
  {
    "module": "/setup/main.exe",
    "blocks": [
      {
        "offset": 1,
        "count": 0
      },
      {
        "offset": 300,
        "count": 1
      },
      {
        "offset": 5000,
        "count": 0
      }
    ]
  },
  {
    "module": "/setup/lib/some.dll",
    "blocks": [
      {
        "offset": 123,
        "count": 0
      },
      {
        "offset": 456,
        "count": 10
      }
    ]
  }
]
"#;

#[test]
fn test_serialize_deseralize() -> Result<()> {
    let value = json!([
        {
            "module": MAIN_EXE,
            "blocks": [
                { "offset": 1, "count": 0 },
                { "offset": 300, "count": 1 },
                { "offset": 5000, "count": 0 },
            ],
        },
        {
            "module": SOME_DLL,
            "blocks": [
                { "offset": 123, "count": 0 },
                { "offset": 456, "count": 10 },
            ],
        },
    ]);
    let coverage: BinaryCoverageJson = serde_json::from_value(value)?;

    let text = serde_json::to_string_pretty(&coverage)?;
    assert_eq!(text.trim(), EXPECTED.trim());

    Ok(())
}
