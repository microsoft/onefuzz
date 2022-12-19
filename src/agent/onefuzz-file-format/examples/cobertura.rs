// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;

use coverage::source::{Count, FileCoverage, Line, SourceCoverage};
use debuggable_module::path::FilePath;
use onefuzz_file_format::coverage::cobertura::CoberturaCoverage;

fn main() -> Result<()> {
    let modoff = vec![
        (r"/missing/lib.c", vec![1, 2, 3, 5, 8]),
        (
            r"test-data/fuzz.c",
            vec![
                7, 8, 10, 13, 16, 17, 21, 22, 23, 27, 28, 29, 30, 32, 33, 37, 39, 42, 44,
            ],
        ),
        (r"test-data\fuzz.h", vec![3, 4, 5]),
        (r"test-data\lib\explode.h", vec![1, 2, 3]),
    ];

    let mut coverage = SourceCoverage::default();

    for (path, lines) in modoff {
        let file_path = FilePath::new(path)?;

        let mut file = FileCoverage::default();

        for line in lines {
            let count = u32::from(line % 3 == 0);
            file.lines.insert(Line::new(line)?, Count(count));
        }

        coverage.files.insert(file_path, file);
    }

    let cobertura = CoberturaCoverage::from(coverage);

    let text = cobertura.to_string()?;
    println!("{text}");

    Ok(())
}
