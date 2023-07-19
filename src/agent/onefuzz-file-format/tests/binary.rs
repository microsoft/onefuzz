// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use pretty_assertions::assert_eq;

use anyhow::Result;
use coverage::binary::{BinaryCoverage, Count, FilePath, ModuleBinaryCoverage, Offset};
use onefuzz_file_format::coverage::binary::BinaryCoverageJson;

fn expected_binary_coverage() -> Result<BinaryCoverage> {
    let main_exe_path = FilePath::new("/setup/main.exe")?;
    let some_dll_path = FilePath::new("/setup/lib/some.dll")?;

    let mut main_exe = ModuleBinaryCoverage::default();
    main_exe.offsets.insert(Offset(1), Count(0));
    main_exe.offsets.insert(Offset(300), Count(1));
    main_exe.offsets.insert(Offset(5000), Count(0));

    let mut some_dll = ModuleBinaryCoverage::default();
    some_dll.offsets.insert(Offset(123), Count(0));
    some_dll.offsets.insert(Offset(456), Count(10));

    let mut binary = BinaryCoverage::default();
    binary.modules.insert(some_dll_path, some_dll);
    binary.modules.insert(main_exe_path, main_exe);

    Ok(binary)
}

#[test]
fn test_binary_coverage_formats() -> Result<()> {
    let expected = expected_binary_coverage()?;

    let v0_text = include_str!("files/binary-coverage.v0.json");
    let v0_json = BinaryCoverageJson::deserialize(v0_text)?;
    let from_v0 = BinaryCoverage::try_from(v0_json)?;
    assert_eq!(from_v0, expected);

    let v1_text = include_str!("files/binary-coverage.v1.json");
    let v1_json = BinaryCoverageJson::deserialize(v1_text)?;
    let from_v1 = BinaryCoverage::try_from(v1_json)?;
    assert_eq!(from_v1, expected);

    Ok(())
}
