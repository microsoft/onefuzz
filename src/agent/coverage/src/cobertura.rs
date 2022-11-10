use crate::source::SourceCoverage;
use crate::source::SourceFileCoverage;
use anyhow::Context;
use anyhow::Error;
use anyhow::Result;
use quick_xml::writer::Writer;
use std::io::Cursor;
use std::path::Path;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

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
pub fn convert_path(file: &SourceFileCoverage) -> String {
    file.file.replace('\\', "/").to_lowercase()
}

// if full file name does not have / , keep full file name
pub fn get_file_name(file: &str) -> String {
    let file_name = match file.split('/').next_back() {
        Some(_file_name) => file.split('/').next_back().unwrap(),
        None => file,
    };
    file_name.to_string()
}

// get directory of file if valid file path, otherwise make package name include and error message
pub fn get_parent_path(path_slash: &str) -> PathBuf {
    let path = Path::new(&path_slash);
    let none_message = "Invalid file format: ".to_owned() + path_slash;
    let parent_path = match path.file_name() {
        Some(_parent_path) => path.parent().unwrap(),
        None => Path::new(&none_message),
    };
    parent_path.to_path_buf()
}

pub fn cobertura(source_coverage: SourceCoverage) -> Result<String, Error> {
    let mut writer = Writer::new_with_indent(Cursor::new(Vec::new()), b' ', 4);

    let unixtime = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .context("system time before unix epoch")?
        .as_secs();

    let coverage_line_values = compute_line_values_coverage(&source_coverage.files);
    writer
        .create_element("coverage")
        .with_attributes([
            (
                "line-rate",
                format!("{:.02}", coverage_line_values.line_rate).as_str(),
            ),
            ("branch-rate", "0"),
            (
                "lines-covered",
                coverage_line_values.hit_lines.to_string().as_str(),
            ),
            (
                "lines-valid",
                coverage_line_values.valid_lines.to_string().as_str(),
            ),
            ("branches-covered", "0"),
            ("branches-valid", "0"),
            ("complexity", "0"),
            ("version", "0.1"),
            ("timestamp", unixtime.to_string().as_str()),
        ])
        .write_inner_content(|writer| {
            writer
                .create_element("packages")
                .write_inner_content(|writer| {
                    // path (excluding file name) is package name for better results with ReportGenerator
                    // class name is only file name (no path)
                    for file in &source_coverage.files {
                        write_file(writer, file)?;
                    }

                    Ok(())
                })?;

            Ok(())
        })?;

    Ok(String::from_utf8(writer.into_inner().into_inner())?)
}

fn write_file(
    writer: &mut Writer<Cursor<Vec<u8>>>,
    file: &SourceFileCoverage,
) -> quick_xml::Result<()> {
    let path = convert_path(file);
    let parent_path = get_parent_path(&path);
    let package_line_values = compute_line_values_package(file);
    let class_name = get_file_name(&path);

    writer
        .create_element("package")
        .with_attributes([
            ("name", parent_path.display().to_string().as_str()),
            (
                "line-rate",
                format!("{:.02}", package_line_values.line_rate).as_str(),
            ),
            ("branch-rate", "0"),
            ("complexity", "0"),
        ])
        .write_inner_content(|writer| {
            writer
                .create_element("classes")
                .write_inner_content(|writer| {
                    writer
                        .create_element("class")
                        .with_attributes([
                            ("name", class_name.as_str()),
                            ("filename", path.as_str()),
                            (
                                "line-rate",
                                format!("{:.02}", package_line_values.line_rate).as_str(),
                            ),
                            ("branch-rate", "0"),
                            ("complexity", "0"),
                        ])
                        .write_inner_content(|writer| {
                            writer
                                .create_element("lines")
                                .write_inner_content(|writer| {
                                    let line_locations = &file.locations;
                                    for location in line_locations {
                                        writer
                                            .create_element("line")
                                            .with_attributes([
                                                ("number", location.line.to_string().as_str()),
                                                ("hits", location.count.to_string().as_str()),
                                                ("branch", "false"),
                                            ])
                                            .write_empty()?;
                                    }
                                    Ok(())
                                })?;
                            Ok(())
                        })?;
                    Ok(())
                })?;
            Ok(())
        })?;
    Ok(())
}

#[cfg(test)]

mod tests {
    use super::*;
    use crate::source::SourceCoverageLocation;
    use anyhow::Result;
    use pretty_assertions::assert_eq;

    #[test]
    fn test_cobertura_conversion_windows_to_posix_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:\\Users\\file1.txt".to_string(),
        };

        let path = convert_path(&file);
        assert_eq!(&path, "c:/users/file1.txt");
    }

    #[test]
    fn test_cobertura_conversion_windows_to_posix_parent_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:\\Users\\file1.txt".to_string(),
        };

        let path = convert_path(&file);
        let parent_path = get_parent_path(&path);
        assert_eq!(&(parent_path.display().to_string()), "c:/users");
    }

    #[test]
    fn test_cobertura_conversion_posix_to_posix_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:/Users/file1.txt".to_string(),
        };

        let path = convert_path(&file);

        assert_eq!(&path, "c:/users/file1.txt");
    }

    #[test]
    fn test_cobertura_conversion_posix_to_posix_parent_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:/Users/file1.txt".to_string(),
        };

        let path = convert_path(&file);
        let parent_path = get_parent_path(&path);

        assert_eq!(&(parent_path.display().to_string()), "c:/users");
    }

    #[test]
    fn test_cobertura_invalid_windows_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:\\Users\\file\\..".to_string(),
        };

        let path = convert_path(&file);

        assert_eq!(&path, "c:/users/file/..");
    }

    #[test]
    fn test_cobertura_invalid_windows_parent_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:\\Users\\file\\..".to_string(),
        };

        let path = convert_path(&file);
        let parent_path = get_parent_path(&path);

        assert_eq!(
            &(parent_path.display().to_string()),
            "Invalid file format: c:/users/file/.."
        );
    }

    #[test]
    fn test_cobertura_invalid_posix_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:/Users/file/..".to_string(),
        };

        let path = convert_path(&file);
        assert_eq!(&path, "c:/users/file/..");
    }

    #[test]
    fn test_cobertura_invalid_posix_parent_path() {
        let coverage_locations_vec1 = vec![SourceCoverageLocation {
            line: 5,
            column: None,
            count: 3,
        }];

        let file = SourceFileCoverage {
            locations: coverage_locations_vec1,
            file: "C:/Users/file/..".to_string(),
        };

        let path = convert_path(&file);
        let parent_path = get_parent_path(&path);

        assert_eq!(
            &(parent_path.display().to_string()),
            "Invalid file format: c:/users/file/.."
        );
    }

    #[test]
    fn test_cobertura_source_to_cobertura_mixed() -> Result<()> {
        let coverage_locations_vec1 = vec![
            SourceCoverageLocation {
                line: 5,
                column: None,
                count: 3,
            },
            SourceCoverageLocation {
                line: 10,
                column: None,
                count: 0,
            },
        ];

        let coverage_locations_vec2 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let coverage_locations_vec3 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 1,
        }];

        let coverage_locations_vec4 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let file_coverage_vec1 = vec![
            SourceFileCoverage {
                locations: coverage_locations_vec1,
                file: "C:\\Users\\file1.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec2,
                file: "C:/Users/file2.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec3,
                file: "C:\\Users\\file\\..".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec4,
                file: "C:/Users/file/..".to_string(),
            },
        ];

        let source_coverage_result = cobertura(SourceCoverage {
            files: file_coverage_vec1,
        });

        let unixtime = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .context("system time before unix epoch")?
            .as_secs();

        let expected = format!(
            r#"<coverage line-rate="0.40" branch-rate="0" lines-covered="2" lines-valid="5" branches-covered="0" branches-valid="0" complexity="0" version="0.1" timestamp="{unixtime}">
    <packages>
        <package name="c:/users" line-rate="0.50" branch-rate="0" complexity="0">
            <classes>
                <class name="file1.txt" filename="c:/users/file1.txt" line-rate="0.50" branch-rate="0" complexity="0">
                    <lines>
                        <line number="5" hits="3" branch="false"/>
                        <line number="10" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="c:/users" line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name="file2.txt" filename="c:/users/file2.txt" line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="1" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
    </packages>
</coverage>"#
        );

        assert_eq!(source_coverage_result?, expected);
        Ok(())
    }

    #[test]
    fn test_cobertura_source_to_cobertura_posix_paths() -> Result<()> {
        let coverage_locations_vec1 = vec![
            SourceCoverageLocation {
                line: 5,
                column: None,
                count: 3,
            },
            SourceCoverageLocation {
                line: 10,
                column: None,
                count: 0,
            },
        ];

        let coverage_locations_vec2 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let coverage_locations_vec3 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 1,
        }];

        let coverage_locations_vec4 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let file_coverage_vec1 = vec![
            SourceFileCoverage {
                locations: coverage_locations_vec1,
                file: "C:/Users/file1.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec2,
                file: "C:/Users/file2.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec3,
                file: "C:/Users/file/..".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec4,
                file: "C:/Users/file/..".to_string(),
            },
        ];

        let source_coverage_result = cobertura(SourceCoverage {
            files: file_coverage_vec1,
        });

        let unixtime = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .context("system time before unix epoch")?
            .as_secs();

        let expected = format!(
            r#"<coverage line-rate="0.40" branch-rate="0" lines-covered="2" lines-valid="5" branches-covered="0" branches-valid="0" complexity="0" version="0.1" timestamp="{unixtime}">
    <packages>
        <package name="c:/users" line-rate="0.50" branch-rate="0" complexity="0">
            <classes>
                <class name="file1.txt" filename="c:/users/file1.txt" line-rate="0.50" branch-rate="0" complexity="0">
                    <lines>
                        <line number="5" hits="3" branch="false"/>
                        <line number="10" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="c:/users" line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name="file2.txt" filename="c:/users/file2.txt" line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="1" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
    </packages>
</coverage>"#
        );

        assert_eq!(source_coverage_result?, expected);
        Ok(())
    }

    #[test]
    fn test_cobertura_source_to_cobertura_windows_paths() -> Result<()> {
        let coverage_locations_vec1 = vec![
            SourceCoverageLocation {
                line: 5,
                column: None,
                count: 3,
            },
            SourceCoverageLocation {
                line: 10,
                column: None,
                count: 0,
            },
        ];

        let coverage_locations_vec2 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let coverage_locations_vec3 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 1,
        }];

        let coverage_locations_vec4 = vec![SourceCoverageLocation {
            line: 1,
            column: None,
            count: 0,
        }];

        let file_coverage_vec1 = vec![
            SourceFileCoverage {
                locations: coverage_locations_vec1,
                file: "C:\\Users\\file1.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec2,
                file: "C:\\Users\\file2.txt".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec3,
                file: "C:\\Users\\file\\..".to_string(),
            },
            SourceFileCoverage {
                locations: coverage_locations_vec4,
                file: "C:\\Users\\file\\..".to_string(),
            },
        ];

        let source_coverage_result = cobertura(SourceCoverage {
            files: file_coverage_vec1,
        });

        let unixtime = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .context("system time before unix epoch")?
            .as_secs();

        let expected = format!(
            r#"<coverage line-rate="0.40" branch-rate="0" lines-covered="2" lines-valid="5" branches-covered="0" branches-valid="0" complexity="0" version="0.1" timestamp="{unixtime}">
    <packages>
        <package name="c:/users" line-rate="0.50" branch-rate="0" complexity="0">
            <classes>
                <class name="file1.txt" filename="c:/users/file1.txt" line-rate="0.50" branch-rate="0" complexity="0">
                    <lines>
                        <line number="5" hits="3" branch="false"/>
                        <line number="10" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="c:/users" line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name="file2.txt" filename="c:/users/file2.txt" line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="1.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="1" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
        <package name="Invalid file format: c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
            <classes>
                <class name=".." filename="c:/users/file/.." line-rate="0.00" branch-rate="0" complexity="0">
                    <lines>
                        <line number="1" hits="0" branch="false"/>
                    </lines>
                </class>
            </classes>
        </package>
    </packages>
</coverage>"#
        );

        assert_eq!(source_coverage_result?, expected);
        Ok(())
    }
}
