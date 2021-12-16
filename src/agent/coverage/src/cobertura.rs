use std::time::{SystemTime, UNIX_EPOCH};   
use xml::writer::{EmitterConfig, XmlEvent};
use anyhow::Context;
use anyhow::Error;
use crate::source::SourceCoverage;
use crate::source::SourceFileCoverage;
use crate::source::SourceCoverageLocation;


pub fn cobertura(source_coverage:SourceCoverage) -> Result<String, Error> {

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
            .attr("lines-valid","0")
            .attr("lines-covered","0")
            .attr("line-rate","0")
            .attr("branches-valid", "0")
            .attr("branches-covered", "0")
            .attr("branch-rate", "0")
            .attr("timestamp", &format!("{}", unixtime))
            .attr("complexity", "0")
            .attr("version", "0.1"),
    )?;
    emitter.write(XmlEvent::start_element("sources"))?;
    emitter.write(XmlEvent::start_element("source"))?;
    emitter.write(XmlEvent::characters(""))?;
    emitter.write(XmlEvent::end_element())?; // source
    emitter.write(XmlEvent::end_element())?; // sources

    emitter.write(XmlEvent::start_element("packages"))?;
    emitter.write(
        XmlEvent::start_element("package")
            .attr("name", "0")
            .attr("lines-valid","0")
            .attr("lines-covered","0")
            .attr("line-rate","0")
            .attr("branches-valid", "0")
            .attr("branches-covered", "0")
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
                .attr("lines-valid","0")
                .attr("lines-covered","0")
                .attr("line-rate","0")
                .attr("branches-valid", "0")
                .attr("branches-covered", "0")
                .attr("branch-rate", "0")
                .attr("complexity", "0"),
        )?; 
            // emitter.write(XmlEvent::start_element("methods"))?;
            // emitter.write(
            //     XmlEvent::start_element("method")
            //         .attr("name", "0")
            //         .attr("signature", "0")
            //         .attr("lines-valid","0")
            //         .attr("lines-covered","0")
            //         .attr("line-rate","0")
            //         .attr("branches-valid", "0")
            //         .attr("branches-covered", "0")
            //         .attr("branch-rate", "0")
            //         .attr("complexity", "0"),
            // )?;
            let locations: Vec<SourceCoverageLocation> = file.locations;
            emitter.write(XmlEvent::start_element("lines"))?;
            for location in locations {
                emitter.write(
                    XmlEvent::start_element("line")
                        .attr("number", &location.line.to_string())
                        .attr("hits", &location.count.to_string())
                        .attr("branch", "false"),
                )?;
            }
            emitter.write(XmlEvent::end_element())?; // line

        emitter.write(XmlEvent::end_element())?; // lines
        // emitter.write(XmlEvent::end_element())?; // method
        // emitter.write(XmlEvent::end_element())?; // methods
        emitter.write(XmlEvent::end_element())?; // class
    }

    emitter.write(XmlEvent::end_element())?; // classes
    emitter.write(XmlEvent::end_element())?; // package

    emitter.write(XmlEvent::end_element())?; // packages
    emitter.write(XmlEvent::end_element())?; // coverage



    Ok(String::from_utf8(backing)?)

}



// #[cfg(test)]

// mod tests {
//     use super::*;
//     use anyhow::Result;

//     #[test]
//     fn test_source_to_cobertura() -> Result<()> {

//         let sourceCoverageLocation1 = SourceCoverageLocation {
//             line:10,
//             count:0
//         };
//         let sourceCoverageLocation2 = SourceCoverageLocation {
//             line:5,
//             count:3
//         };

//         let coverageLocations_vec1: Vec<SourceCoverageLocation> = Vec::new();
//         coverageLocations_vec1.push(sourceCoverageLocation1);
//         coverageLocations_vec1.push(sourceCoverageLocation2);

//         let coverageLocations_vec2: Vec<SourceCoverageLocation> = Vec::new();
//         coverageLocations_vec2.push(sourceCoverageLocation1);
//         coverageLocations_vec2.push(sourceCoverageLocation2);

//         let sourceFileCoverage1 = SourceFileCoverage {
//             locations:coverageLocations_vec1,
//             file:"C:/Users/file1.txt".to_string()
//         };

//         let sourceFileCoverage2 = SourceFileCoverage {
//             locations:coverageLocations_vec2,
//             file:"C:/Users/file2.txt".to_string()
//         };

//         let fileCoverage_vec1: Vec<SourceFileCoverage> = Vec::new();
//         fileCoverage_vec1.push(sourceFileCoverage1);
//         fileCoverage_vec1.push(sourceFileCoverage2);

//         let sourceCoverage1 = SourceCoverage {
//             files:fileCoverage_vec1
//         };
        
//         let _result = cobertura (sourceCoverage1);

        // let mut backing_test: Vec<u8> = Vec::new();
        // let mut emitter_test = EmitterConfig::new()
        //     .perform_indent(true)
        //     .create_writer(&mut backing_test);

        // let unixtime = SystemTime::now()
        // .duration_since(UNIX_EPOCH)
        // .context("system time before unix epoch")?
        // .as_secs();

        // emitter_test.write(
        //     XmlEvent::start_element("coverage")
        //         .attr("lines-valid","0")
        //         .attr("lines-covered","0")
        //         .attr("line-rate","0")
        //         .attr("branches-valid", "0")
        //         .attr("branches-covered", "0")
        //         .attr("branch-rate", "0")
        //         .attr("timestamp", &format!("{}", unixtime))
        //         .attr("complexity", "0")
        //         .attr("version", "0.1"),
        // )?;
        // emitter_test.write(XmlEvent::start_element("sources"))?;
        // emitter_test.write(XmlEvent::start_element("source"))?;
        // emitter_test.write(XmlEvent::characters(""))?;
        // emitter_test.write(XmlEvent::end_element())?; // source
        // emitter_test.write(XmlEvent::end_element())?; // sources
    
        // emitter_test.write(XmlEvent::start_element("packages"))?;
        // emitter_test.write(
        //     XmlEvent::start_element("package")
        //         .attr("name", "0")
        //         .attr("lines-valid","0")
        //         .attr("lines-covered","0")
        //         .attr("line-rate","0")
        //         .attr("branches-valid", "0")
        //         .attr("branches-covered", "0")
        //         .attr("branch-rate", "0")
        //         .attr("complexity", "0"),
        // )?;
        // emitter_test.write(XmlEvent::start_element("classes"))?;
        // emitter_test.write(
        //     XmlEvent::start_element("class")
        //         .attr("name", "0")
        //         .attr("filename", "C:/Users/file1.txt")
        //         .attr("lines-valid","0")
        //         .attr("lines-covered","0")
        //         .attr("line-rate","0")
        //         .attr("branches-valid", "0")
        //         .attr("branches-covered", "0")
        //         .attr("branch-rate", "0")
        //         .attr("complexity", "0"),
        // emitter_test.write(XmlEvent::start_element("lines"))?;
        // emitter_test.write(
        //     XmlEvent::start_element("line")
        //         .attr("number", "10")
        //         .attr("hits", "0")
        //         .attr("branch", "false"),
        // )?;
        //     emitter_test.write(
        //     XmlEvent::start_element("class")
        //         .attr("name", "0")
        //         .attr("filename", "C:/Users/file2.txt")
        //         .attr("lines-valid","0")
        //         .attr("lines-covered","0")
        //         .attr("line-rate","0")
        //         .attr("branches-valid", "0")
        //         .attr("branches-covered", "0")
        //         .attr("branch-rate", "0")
        //         .attr("complexity", "0"),
        // emitter_test.write(XmlEvent::start_element("lines"))?;
        // emitter_test.write(
        //     XmlEvent::start_element("line")
        //         .attr("number", "5")
        //         .attr("hits", "3")
        //         .attr("branch", "false"),
        // )?;
        // emitter_test.write(XmlEvent::end_element())?; // line

        // emitter_test.write(XmlEvent::end_element())?; // lines
        // emitter_test.write(XmlEvent::end_element())?; // class
        // emitter_test.write(XmlEvent::end_element())?; // classes
        // emitter_test.write(XmlEvent::end_element())?; // package
        // emitter_test.write(XmlEvent::end_element())?; // packages
        // emitter_test.write(XmlEvent::end_element())?; // coverage

        // assert_eq!();
        // Ok(())
    // }
// }




