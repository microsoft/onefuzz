use crate::source::SourceCoverage;
use crate::source::SourceCoverageLocation;
use crate::source::SourceFileCoverage;
use anyhow::Context;
use anyhow::Error;
use anyhow::Result;
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

    emitter.write(
        XmlEvent::start_element("coverage")
            .attr("line-rate", "0")
            .attr("branch-rate", "0")
            .attr("lines-covered", "0")
            .attr("lines-valid", "0")
            .attr("branches-covered", "0")
            .attr("branches-valid", "0")
            .attr("complexity", "0")
            .attr("version", "0.1")
            .attr("timestamp", &format!("{}", unixtime)),
    )?;

    emitter.write(XmlEvent::start_element("packages"))?;
    emitter.write(
        XmlEvent::start_element("package")
            .attr("name", "0")
            .attr("line-rate", "0")
            .attr("branch-rate", "0")
            .attr("complexity", "0"),
    )?;

    emitter.write(XmlEvent::start_element("classes"))?;
    // loop through files
    let files: Vec<SourceFileCoverage> = source_coverage.files;
    for file in files {
        emitter.write(
            XmlEvent::start_element("class")
                .attr("name", "0")
                .attr("filename", &file.file)
                .attr("line-rate", "0")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        let locations: Vec<SourceCoverageLocation> = file.locations;
        emitter.write(XmlEvent::start_element("lines"))?;
        for location in locations {
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
    }

    emitter.write(XmlEvent::end_element())?; // classes
    emitter.write(XmlEvent::end_element())?; // package
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
            line: 0,
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
                .attr("line-rate", "0")
                .attr("branch-rate", "0")
                .attr("lines-covered", "0")
                .attr("lines-valid", "0")
                .attr("branches-covered", "0")
                .attr("branches-valid", "0")
                .attr("complexity", "0")
                .attr("version", "0.1")
                .attr("timestamp", &format!("{}", unixtime)),
        )?;

        _emitter_test.write(XmlEvent::start_element("packages"))?;
        _emitter_test.write(
            XmlEvent::start_element("package")
                .attr("name", "0")
                .attr("line-rate", "0")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("classes"))?;

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "0")
                .attr("filename", "C:/Users/file1.txt")
                .attr("line-rate", "0")
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

        _emitter_test.write(
            XmlEvent::start_element("class")
                .attr("name", "0")
                .attr("filename", "C:/Users/file2.txt")
                .attr("line-rate", "0")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?;

        _emitter_test.write(XmlEvent::start_element("lines"))?;

        _emitter_test.write(
            XmlEvent::start_element("line")
                .attr("number", "0")
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

        assert_eq!(source_coverage_result?, String::from_utf8(backing_test)?);

        Ok(())
    }
}
