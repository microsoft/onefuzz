// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use pretty_assertions::assert_eq;

use anyhow::Result;
use coverage::source::{Count, Line, SourceCoverage};
use debuggable_module::path::FilePath;
use onefuzz_file_format::coverage::source::SourceCoverageJson;

fn expected_source_coverage() -> Result<SourceCoverage> {
    let main_path = FilePath::new("src/bin/main.c")?;
    let common_path = FilePath::new("src/lib/common.c")?;

    let mut source = SourceCoverage::default();

    let main = source.files.entry(main_path).or_default();
    main.lines.insert(Line::new(4)?, Count(1));
    main.lines.insert(Line::new(9)?, Count(0));
    main.lines.insert(Line::new(12)?, Count(5));

    let common = source.files.entry(common_path).or_default();
    common.lines.insert(Line::new(5)?, Count(1));
    common.lines.insert(Line::new(8)?, Count(0));

    Ok(source)
}

#[test]
fn test_source_coverage_formats() -> Result<()> {
    let expected = expected_source_coverage()?;

    let v0_text = include_str!("files/source-coverage.v0.json");
    let v0_json = SourceCoverageJson::deserialize(v0_text)?;
    let from_v0 = SourceCoverage::try_from(v0_json)?;
    assert_eq!(from_v0, expected);

    let v1_text = include_str!("files/source-coverage.v1.json");
    let v1_json = SourceCoverageJson::deserialize(v1_text)?;
    let from_v1 = SourceCoverage::try_from(v1_json)?;
    assert_eq!(from_v1, expected);

    Ok(())
}
