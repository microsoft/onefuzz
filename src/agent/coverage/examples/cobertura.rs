// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;

use cobertura::CoberturaCoverage;
use coverage::source::{Count, FileCoverage, Line, SourceCoverage};
use debuggable_module::path::FilePath;

fn main() -> Result<()> {
    println!("{}", generate_output()?.to_string()?);
    Ok(())
}

fn generate_output() -> Result<CoberturaCoverage> {
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

    Ok(CoberturaCoverage::from(&coverage))
}

#[cfg(test)]
mod test {
    use super::*;
    use pretty_assertions::assert_eq;

    #[test]
    // On Windows this produces different output due to filename parsing.
    #[cfg(target_os = "linux")]
    pub fn check_output() {
        let result = generate_output().unwrap().to_string().unwrap();

        let expected = r#"<coverage line-rate="0.30" branch-rate="0.00" lines-covered="9" lines-valid="30" branches-covered="0" branches-valid="0" complexity="0" version="" timestamp="0">
  <sources>
    <source path="test-data\fuzz.h"/>
    <source path="test-data\lib\explode.h"/>
    <source path="/missing/lib.c"/>
    <source path="test-data/fuzz.c"/>
  </sources>
  <packages>
    <package name="" line-rate="0.33" branch-rate="0.00" complexity="0">
      <classes>
        <class name="test-data\fuzz.h" filename="test-data\fuzz.h" line-rate="0.33" branch-rate="0.00" complexity="0">
          <methods>
          </methods>
          <lines>
            <line number="3" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="4" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="5" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
          </lines>
        </class>
        <class name="test-data\lib\explode.h" filename="test-data\lib\explode.h" line-rate="0.33" branch-rate="0.00" complexity="0">
          <methods>
          </methods>
          <lines>
            <line number="1" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="2" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="3" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
          </lines>
        </class>
      </classes>
    </package>
    <package name="/missing" line-rate="0.20" branch-rate="0.00" complexity="0">
      <classes>
        <class name="lib.c" filename="/missing/lib.c" line-rate="0.20" branch-rate="0.00" complexity="0">
          <methods>
          </methods>
          <lines>
            <line number="1" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="2" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="3" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="5" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="8" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
          </lines>
        </class>
      </classes>
    </package>
    <package name="test-data" line-rate="0.32" branch-rate="0.00" complexity="0">
      <classes>
        <class name="fuzz.c" filename="test-data/fuzz.c" line-rate="0.32" branch-rate="0.00" complexity="0">
          <methods>
          </methods>
          <lines>
            <line number="7" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="8" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="10" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="13" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="16" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="17" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="21" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="22" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="23" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="27" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="28" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="29" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="30" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="32" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="33" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="37" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="39" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="42" hits="1" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
            <line number="44" hits="0" branch="false" condition-coverage="100%">
              <conditions>
              </conditions>
            </line>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>"#;

        assert_eq!(expected, result);
    }

    #[tokio::test]
    async fn sync_and_async_are_identical() {
        let sync_output = generate_output().unwrap().to_string().unwrap();
        let async_output = generate_output().unwrap().to_string_async().await.unwrap();

        assert_eq!(sync_output, async_output);
    }
}
