// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    collections::{BTreeMap, BTreeSet},
    iter::Sum,
};

use cobertura::{
    Class, Classes, CoberturaCoverage, Line, Lines, Package, Packages, Source, Sources,
};
use debuggable_module::path::FilePath;

use crate::source::SourceCoverage;

// Dir -> Set<FilePath>
type FileMap<'a> = BTreeMap<&'a str, BTreeSet<&'a FilePath>>;

impl From<SourceCoverage> for CoberturaCoverage {
    fn from(source: SourceCoverage) -> Self {
        // The Cobertura data model is organized around `classes` and `methods` contained
        // in `packages`. Our source coverage has no language-level assumptions.
        //
        // To obtain legible HTML reports using ReportGenerator, will we use `<package>`
        // elements to group files by their parent directory. Each measured source file
        // will be represented a `<class>`. The and the measured source file's lines will
        // become `<line>` elements of the (synthetic) class.
        //
        // Note: ReportGenerator automatically computes and rolls up aggregated coverage
        // stats. We do _not_ need to manually compute any `line-rate` attributes. The
        // presence of these attributes is required by the Cobertura schema, but even if
        // they are set to 0 (as in our `Default` impls), ReportGenerator ignores them.

        // Source files grouped by directory.
        let mut file_map = FileMap::default();
        for file_path in source.files.keys() {
            let dir = file_path.directory();
            let files = file_map.entry(dir).or_default();
            files.insert(file_path);
        }

        // Collect every file name for the `<sources>` manifest element.
        let sources = file_map
            .values()
            .flatten()
            .map(|file_path| Source {
                path: file_path.to_string(),
            })
            .collect();

        // Iterate through the grouped files, accumulating `<package>` elements.
        let (packages, hit_counts): (Vec<Package>, Vec<HitCounts>) = file_map
            .into_iter()
            .map(|(directory, files)| directory_to_package(&source, directory, files))
            .unzip();

        let hit_count: HitCounts = hit_counts.into_iter().sum();

        CoberturaCoverage {
            sources: Some(Sources { sources }),
            packages: Packages { packages },
            line_rate: hit_count.rate(),
            lines_covered: hit_count.hit_lines,
            lines_valid: hit_count.total_lines,
            ..CoberturaCoverage::default()
        }
    }
}

// Make a `<package>` to represent the directory.
//
// We will add a `<class>` for each contained file.
fn directory_to_package(
    source: &SourceCoverage,
    directory: &str,
    files: BTreeSet<&FilePath>,
) -> (Package, HitCounts) {
    let (classes, hit_counts): (Vec<Class>, Vec<HitCounts>) = files
        .into_iter()
        .map(|file_path| file_to_class(source, file_path))
        .unzip();

    let hit_count: HitCounts = hit_counts.into_iter().sum();

    let result = Package {
        name: directory.to_owned(),
        classes: Classes { classes },
        line_rate: hit_count.rate(),
        ..Package::default()
    };

    (result, hit_count)
}

// Make a `<class>` to represent a file.
fn file_to_class(source: &SourceCoverage, file_path: &FilePath) -> (Class, HitCounts) {
    let lines: Vec<Line> = source.files[file_path] // can't panic, by construction
        .lines
        .iter()
        .map(|(line, count)| Line {
            number: u64::from(line.number()),
            hits: u64::from(count.0),
            ..Line::default()
        })
        .collect();

    let hit_counts = HitCounts {
        hit_lines: lines.iter().filter(|l| l.hits > 0).count() as u64,
        total_lines: lines.len() as u64,
    };

    let result = Class {
        name: file_path.file_name().to_owned(),
        filename: file_path.to_string(),
        lines: Lines { lines },
        line_rate: hit_counts.rate(),
        ..Class::default()
    };

    (result, hit_counts)
}

struct HitCounts {
    hit_lines: u64,
    total_lines: u64,
}

impl HitCounts {
    fn rate(&self) -> f64 {
        self.hit_lines as f64 / self.total_lines as f64
    }
}

impl Sum for HitCounts {
    fn sum<I: Iterator<Item = Self>>(iter: I) -> Self {
        iter.fold(
            HitCounts {
                hit_lines: 0,
                total_lines: 0,
            },
            |current, next| HitCounts {
                hit_lines: current.hit_lines + next.hit_lines,
                total_lines: current.total_lines + next.total_lines,
            },
        )
    }
}
