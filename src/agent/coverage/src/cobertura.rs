use crate::source::SourceCoverage;
use crate::source::SourceCoverageLocation;
use crate::source::SourceFileCoverage;
use anyhow::Context;
use anyhow::Error;
use anyhow::Result;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};
use xml::writer::{EmitterConfig, XmlEvent};

pub fn cobertura(source_coverage: SourceCoverage) -> Result<String, Error> {
    let mut backing: Vec<u8> = Vec::new();
    let mut emitter = EmitterConfig::new()
        .perform_indent(true)
        .create_writer(&mut backing);

    let unixtime = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .context("system time before unix epoch")?
        .as_secs();

    // compute line rate, lines-covered, lines-valid by summing total lines and summing lines that have at least 1 hit, will do this for overall, and for each file (package and class in this case)
    let mut total_valid_lines = 0;
    let mut total_hit_lines = 0;
    let mut total_line_rate = 0_f32;
    let copy_source_coverage: SourceCoverage = source_coverage.clone();
    let coverage_files: Vec<SourceFileCoverage> = source_coverage.files;
    for file in coverage_files {
        let locations: Vec<SourceCoverageLocation> = file.locations;
        for location in locations {
            total_valid_lines+=1;
            if &location.count > &0 {
              total_hit_lines+=1;
            }
        }
    }
    if total_valid_lines > 0 {
        total_line_rate = total_hit_lines as f32 /total_valid_lines as f32;  
    }


    emitter.write(
        XmlEvent::start_element("coverage")
            .attr("line-rate", &format!("{:.02}", total_line_rate))
            .attr("branch-rate", "0")
            .attr("lines-covered", &format!("{}", total_hit_lines))
            .attr("lines-valid", &format!("{}", total_valid_lines))
            .attr("branches-covered", "0")
            .attr("branches-valid", "0")
            .attr("complexity", "0")
            .attr("version", "0.1")
            .attr("timestamp", &format!("{}", unixtime)),
    )?;

    emitter.write(XmlEvent::start_element("packages"))?;
    // loop through files, path (excluding file name) will be package name for better results with ReportGenerator
    let package_files: Vec<SourceFileCoverage> = copy_source_coverage.files;
    let mut package_valid_lines = 0;
    let mut package_hit_lines = 0;
    let mut package_line_rate = 0_f32;
    for file in package_files {
        let copy_file = file.clone();
        let package_locations: Vec<SourceCoverageLocation> = file.locations;
        for location in package_locations {
            package_valid_lines+=1;
            if &location.count > &0 {
              package_hit_lines+=1;
            }
        }
        if package_valid_lines > 0 {
          package_line_rate = package_hit_lines as f32 /package_valid_lines as f32;
        }
        let full_path_file = Path::new(&file.file);
        let path = full_path_file.parent().unwrap();
        emitter.write(
            XmlEvent::start_element("package")
                .attr("name", &path.display().to_string())
                .attr("line-rate", &format!("{:.02}",package_line_rate))
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;
        emitter.write(XmlEvent::start_element("classes"))?;
        emitter.write(
            XmlEvent::start_element("class")
                .attr("name", &file.file)
                .attr("filename", &file.file)
                .attr("line-rate", &format!("{:.02}",package_line_rate))
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;
        package_valid_lines = 0;
        package_hit_lines = 0;
        package_line_rate = 0_f32;
        emitter.write(XmlEvent::start_element("lines"))?;
        let line_locations: Vec<SourceCoverageLocation> = copy_file.locations;
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

        let mut file_coverage_vec1: Vec<SourceFileCoverage> = Vec::new();
        file_coverage_vec1.push(SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:/Users/file1.txt".to_string(),
        });
        file_coverage_vec1.push(SourceFileCoverage {
            locations: coverage_locations_vec2,
            file: "C:/Users/file2.txt".to_string(),
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
                .attr("line-rate", "0.33")
                .attr("branch-rate", "0")
                .attr("lines-covered", "1")
                .attr("lines-valid", "3")
                .attr("branches-covered", "0")
                .attr("branches-valid", "0")
                .attr("complexity", "0")
                .attr("version", "0.1")
                .attr("timestamp", &format!("{}", unixtime)),
        )?;

        _emitter_test.write(XmlEvent::start_element("packages"))?;

        _emitter_test.write(
            XmlEvent::start_element("package")
                .attr("name", "C:/Users")
                .attr("line-rate", "0.50")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "C:/Users/file1.txt")
                .attr("filename", "C:/Users/file1.txt")
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
                .attr("name", "C:/Users")
                .attr("line-rate", "0.00")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "C:/Users/file2.txt")
                .attr("filename", "C:/Users/file2.txt")
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
        _emitter_test.write(XmlEvent::end_element())?; // packages
        _emitter_test.write(XmlEvent::end_element())?; // coverage

        //println!("{}", source_coverage_result?);
        assert_eq!(source_coverage_result?, String::from_utf8(backing_test)?);
        //assert!(true);

        Ok(())
    }
}
