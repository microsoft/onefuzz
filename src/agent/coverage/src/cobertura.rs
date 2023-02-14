// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};

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

        // Iterate through the grouped files, accumulating `<package>` elements.
        let mut packages = vec![];
        let mut sources = vec![];

        for (directory, files) in file_map {
            // Make a `<package>` to represent the directory.
            //
            // We will add a `<class>` for each contained file.
            let mut package = Package {
                name: directory.to_owned(),
                ..Package::default()
            };

            let mut classes = vec![];

            for file_path in files {
                // Add the file to the `<sources>` manifest element.
                let src = Source {
                    path: file_path.to_string(),
                };
                sources.push(src);

                let mut lines = vec![];

                // Can't panic, by construction.
                let file_coverage = &source.files[file_path];

                for (line, count) in &file_coverage.lines {
                    let number = u64::from(line.number());
                    let hits = u64::from(count.0);

                    let line = Line {
                        number,
                        hits,
                        ..Line::default()
                    };

                    lines.push(line);
                }

                let class = Class {
                    name: file_path.file_name().to_owned(),
                    filename: file_path.to_string(),
                    lines: Lines { lines },
                    ..Class::default()
                };

                classes.push(class);
            }

            package.classes = Classes { classes };

            packages.push(package);
        }

        CoberturaCoverage {
            sources: Some(Sources { sources }),
            packages: Packages { packages },
            ..CoberturaCoverage::default()
        }
    }
}
