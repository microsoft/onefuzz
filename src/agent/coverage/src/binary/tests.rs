// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use debuggable_module::path::FilePath;
use pretty_assertions::assert_eq;

use crate::binary::{Count, Offset};

use super::*;

macro_rules! module {
    ( $( $offset: expr => $count: expr, )* ) => {{
        let mut module = ModuleBinaryCoverage::default();

        $(
            module.offsets.insert(Offset($offset), Count($count));
        )*

        module
    }}
}

macro_rules! coverage {
    ( $( $path: expr => { $( $offset: expr => $count: expr, )* }, )* ) => {{
        let mut coverage = BinaryCoverage::default();

        $(
            let path = FilePath::new($path)?;
            let module = module! { $( $offset => $count, )* };
            coverage.modules.insert(path, module);
        )*

        coverage
    }}
}

#[test]
fn test_module_increment() -> Result<()> {
    let mut module = module! {
        1 => 1,
        2 => 0,
    };

    module.increment(Offset(2));

    assert_eq!(
        module,
        module! {
            1 => 1,
            2 => 1,
        }
    );

    module.increment(Offset(2));

    assert_eq!(
        module,
        module! {
            1 => 1,
            2 => 2,
        }
    );

    module.increment(Offset(3));

    assert_eq!(
        module,
        module! {
            1 => 1,
            2 => 2,
            3 => 1,
        }
    );

    Ok(())
}

#[test]
fn test_coverage_add() -> Result<()> {
    let mut coverage = coverage! {
        "main.exe" => {
            1 => 1,
            2 => 0,
            3 => 1,
            4 => 0,
        },
        "old.dll" => {
            1 => 0,
        },
    };

    coverage.add(&coverage! {
        "main.exe" => {
            1 => 1,
            2 => 1,
            5 => 1,
        },
        "new.dll" => {
            1 => 1,
        },
    });

    assert_eq!(
        coverage,
        coverage! {
            "main.exe" => {
                1 => 2,
                2 => 1,
                3 => 1,
                4 => 0,
                5 => 1,
            },
            "old.dll" => {
                1 => 0,
            },
            "new.dll" => {
                1 => 1,
            },
        }
    );

    Ok(())
}

#[test]
fn test_coverage_merge() -> Result<()> {
    let mut coverage = coverage! {
        "main.exe" => {
            1 => 1,
            2 => 0,
            3 => 1,
            4 => 0,
        },
        "old.dll" => {
            1 => 0,
        },
    };

    coverage.merge(&coverage! {
        "main.exe" => {
            1 => 1,
            2 => 1,
            5 => 1,
        },
        "new.dll" => {
            1 => 1,
        },
    });

    assert_eq!(
        coverage,
        coverage! {
            "main.exe" => {
                1 => 1,
                2 => 1,
                3 => 1,
                4 => 0,
                5 => 1,
            },
            "old.dll" => {
                1 => 0,
            },
            "new.dll" => {
                1 => 1,
            },
        }
    );

    Ok(())
}
