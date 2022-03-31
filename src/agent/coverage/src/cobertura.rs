use crate::source::SourceCoverage;
use crate::source::SourceFileCoverage;
use anyhow::Context;
use anyhow::Error;
use anyhow::Result;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};
use xml::writer::{EmitterConfig, XmlEvent};

pub struct LineValues {
    pub valid_lines: u64,
    pub hit_lines: u64,
    pub line_rate: f64,
}

impl LineValues {
    pub fn new(valid_lines: u64, hit_lines: u64) -> Self {
        let line_rate = if valid_lines == 0 {
            0.0
        } else {
            (hit_lines as f64) / (valid_lines as f64)
        };

        Self {
            valid_lines,
            hit_lines,
            line_rate,
        }
    }
}

// compute line values (total) for coverage xml element
pub fn compute_line_values_coverage(files: &[SourceFileCoverage]) -> LineValues {
    let mut valid_lines = 0;
    let mut hit_lines = 0;
    for file in files {
        let file_line_values = compute_line_values_package(file);
        valid_lines += file_line_values.valid_lines;
        hit_lines += file_line_values.hit_lines;
    }
    LineValues::new(valid_lines, hit_lines)
}

// compute line values for individual file package xml element
pub fn compute_line_values_package(file: &SourceFileCoverage) -> LineValues {
    let mut valid_lines = 0;
    let mut hit_lines = 0;
    for location in &file.locations {
        valid_lines += 1;
        if location.count > 0 {
            hit_lines += 1;
        }
    }
    LineValues::new(valid_lines, hit_lines)
}

pub fn cobertura(source_coverage: SourceCoverage) -> Result<String, Error> {
    let mut backing: Vec<u8> = Vec::new();
    let mut emitter = EmitterConfig::new()
        .perform_indent(true)
        .create_writer(&mut backing);

    let unixtime = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .context("system time before unix epoch")?
        .as_secs();

    let coverage_line_values = compute_line_values_coverage(&source_coverage.files);

    emitter.write(
        XmlEvent::start_element("coverage")
            .attr(
                "line-rate",
                &format!("{:.02}", coverage_line_values.line_rate),
            )
            .attr("branch-rate", "0")
            .attr(
                "lines-covered",
                &format!("{}", coverage_line_values.hit_lines),
            )
            .attr(
                "lines-valid",
                &format!("{}", coverage_line_values.valid_lines),
            )
            .attr("branches-covered", "0")
            .attr("branches-valid", "0")
            .attr("complexity", "0")
            .attr("version", "0.1")
            .attr("timestamp", &format!("{}", unixtime)),
    )?;

    emitter.write(XmlEvent::start_element("packages"))?;
    // path (excluding file name) will be package name for better results with ReportGenerator
    // class name will be full file path and name
    for file in &source_coverage.files {
        let package_line_values = compute_line_values_package(file);
        let path = Path::new(&file.file);
        let _file_check = path.file_name();
        let none_message = "Invalid file format: ".to_owned() + &file.file;
        let parent_path = match _file_check {
            Some(_file_check) => path.parent().unwrap(),
            None => Path::new(&none_message),
        };

        emitter.write(
            XmlEvent::start_element("package")
                .attr("name", &(parent_path.display().to_string()))
                .attr(
                    "line-rate",
                    &format!("{:.02}", package_line_values.line_rate),
                )
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;
        emitter.write(XmlEvent::start_element("classes"))?;
        emitter.write(
            XmlEvent::start_element("class")
                .attr("name", &file.file)
                .attr("filename", &file.file)
                .attr(
                    "line-rate",
                    &format!("{:.02}", package_line_values.line_rate),
                )
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;
        emitter.write(XmlEvent::start_element("lines"))?;
        let line_locations = &file.locations;
        for location in line_locations {
            emitter.write(
                XmlEvent::start_element("line")
                    .attr("number", &location.line.to_string())
                    .attr("hits", &location.count.to_string())
                    .attr("branch", "false"),
            )?;
            emitter.write(XmlEvent::end_element())?; // line
        }
        emitter.write(XmlEvent::end_element())?; // lines
        emitter.write(XmlEvent::end_element())?; // class

        emitter.write(XmlEvent::end_element())?; // classes
        emitter.write(XmlEvent::end_element())?; // package
    }

    emitter.write(XmlEvent::end_element())?; // packages
    emitter.write(XmlEvent::end_element())?; // coverage

    Ok(String::from_utf8(backing)?)
}

#[cfg(test)]

mod tests {
    use super::*;
    use crate::source::SourceCoverageLocation;
    use anyhow::Result;

    #[test]
    fn test_source_to_cobertura() -> Result<()> {
        let mut coverage_locations_vec1: Vec<SourceCoverageLocation> = Vec::new();
        coverage_locations_vec1.push(SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        });
        coverage_locations_vec1.push(SourceCoverageLocation {
            line: 10,
            column: None,
            count: 0,
        });

        let mut coverage_locations_vec2: Vec<SourceCoverageLocation> = Vec::new();
        coverage_locations_vec2.push(SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        });

        let mut coverage_locations_vec3: Vec<SourceCoverageLocation> = Vec::new();
        coverage_locations_vec3.push(SourceCoverageLocation {
            line: 1,
            column: None,
            count: 1,
        });

        let mut file_coverage_vec1: Vec<SourceFileCoverage> = Vec::new();
        file_coverage_vec1.push(SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:\\Users\\file1.txt".to_string(),
        });
        file_coverage_vec1.push(SourceFileCoverage {
            locations: coverage_locations_vec2,
            file: "C:\\Users\\file2.txt".to_string(),
        });
        file_coverage_vec1.push(SourceFileCoverage {
            locations: coverage_locations_vec3,
            file: "C:\\Users\\file\\..".to_string(),
        });

        let source_coverage_result = cobertura(SourceCoverage {
            files: file_coverage_vec1,
        });

        let mut backing_test: Vec<u8> = Vec::new();
        let mut _emitter_test = EmitterConfig::new()
            .perform_indent(true)
            .create_writer(&mut backing_test);

        let unixtime = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .context("system time before unix epoch")?
            .as_secs();

        _emitter_test.write(
            XmlEvent::start_element("coverage")
                .attr("line-rate", "0.50")
                .attr("branch-rate", "0")
                .attr("lines-covered", "2")
                .attr("lines-valid", "4")
                .attr("branches-covered", "0")
                .attr("branches-valid", "0")
                .attr("complexity", "0")
                .attr("version", "0.1")
                .attr("timestamp", &format!("{}", unixtime)),
        )?;

        _emitter_test.write(XmlEvent::start_element("packages"))?;

        _emitter_test.write(
            XmlEvent::start_element("package")
                .attr("name", "C:\\Users")
                .attr("line-rate", "0.50")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "C:\\Users\\file1.txt")
                .attr("filename", "C:\\Users\\file1.txt")
                .attr("line-rate", "0.50")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;
        _emitter_test.write(XmlEvent::start_element("lines"))?;

        _emitter_test.write(
            XmlEvent::start_element("line")
                .attr("number", "5")
                .attr("hits", "3")
                .attr("branch", "false"),
        )?;
        _emitter_test.write(XmlEvent::end_element())?; // line

        _emitter_test.write(
            XmlEvent::start_element("line")
                .attr("number", "10")
                .attr("hits", "0")
                .attr("branch", "false"),
        )?;

        _emitter_test.write(XmlEvent::end_element())?; // line
        _emitter_test.write(XmlEvent::end_element())?; // lines
        _emitter_test.write(XmlEvent::end_element())?; // class
        _emitter_test.write(XmlEvent::end_element())?; // classes
        _emitter_test.write(XmlEvent::end_element())?; // package

        _emitter_test.write(
            XmlEvent::start_element("package")
                .attr("name", "C:\\Users")
                .attr("line-rate", "0.00")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "C:\\Users\\file2.txt")
                .attr("filename", "C:\\Users\\file2.txt")
                .attr("line-rate", "0.00")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("lines"))?;

        _emitter_test.write(
            XmlEvent::start_element("line")
                .attr("number", "1")
                .attr("hits", "0")
                .attr("branch", "false"),
        )?;

        _emitter_test.write(XmlEvent::end_element())?; // line
        _emitter_test.write(XmlEvent::end_element())?; // lines
        _emitter_test.write(XmlEvent::end_element())?; // class
        _emitter_test.write(XmlEvent::end_element())?; // classes
        _emitter_test.write(XmlEvent::end_element())?; // package

        _emitter_test.write(
            XmlEvent::start_element("package")
                .attr("name", "Invalid file format: C:\\Users\\file\\..")
                .attr("line-rate", "1.00")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "C:\\Users\\file\\..")
                .attr("filename", "C:\\Users\\file\\..")
                .attr("line-rate", "1.00")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("lines"))?;

        _emitter_test.write(
            XmlEvent::start_element("line")
                .attr("number", "1")
                .attr("hits", "1")
                .attr("branch", "false"),
        )?;

        _emitter_test.write(XmlEvent::end_element())?; // line
        _emitter_test.write(XmlEvent::end_element())?; // lines
        _emitter_test.write(XmlEvent::end_element())?; // class
        _emitter_test.write(XmlEvent::end_element())?; // classes
        _emitter_test.write(XmlEvent::end_element())?; // package
        _emitter_test.write(XmlEvent::end_element())?; // packages
        _emitter_test.write(XmlEvent::end_element())?; // coverage

        assert_eq!(source_coverage_result?, String::from_utf8(backing_test)?);

        Ok(())
    }
}
