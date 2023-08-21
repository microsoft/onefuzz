use std::{
    env,
    path::{Path, PathBuf},
};

use onefuzz_task_lib::local::template;
use tempfile::tempdir;

const TEMPLATES_PATH: &str = "./tests/templates";

#[tokio::test]
#[cfg_attr(not(feature = "integration_test"), ignore)]
async fn test_libfuzzer_basic_template() {
    let config = PathBuf::from(TEMPLATES_PATH).join("libfuzzer_basic.yml");
    let libfuzzer_target = get_libfuzzer_target();

    let test_directory = tempdir().expect("Failed to create temporary directory");
    let inputs_directory = PathBuf::from(test_directory.path()).join("inputs");
    let crashes_directory = PathBuf::from(test_directory.path()).join("crashes");
    let crashdumps_directory = PathBuf::from(test_directory.path()).join("inputs");
    let coverage_directory = PathBuf::from(test_directory.path()).join("inputs");
    let regression_reports_directory = PathBuf::from(test_directory.path()).join("inputs");

    // Create a temporary directory for the test
    // Copy the target, template and create the necessary folders
    // Run the template, for a minute?
    // Verify it didn't terminate early
    // Verify the exist code (should be sigkill since we're killing the process?)
    // Check the contents of the folders
    template::launch(config, None).await;
}

// TODO Make this async?
fn get_libfuzzer_target() -> PathBuf {
    if let Ok(target_path) = env::var("ONEFUZZ_TEST_LIBFUZZER_TARGET") {
        let target_path = PathBuf::from(target_path);

        assert!(
            target_path.exists(),
            "The libfuzzer target does not exist. ONEFUZZ_TEST_LIBFUZZER_TARGET = {:?}",
            target_path
        );
        assert!(
            target_path.is_file(),
            "The libfuzzer target is not a file. ONEFUZZ_TEST_LIBFUZZER_TARGET = {:?}",
            target_path
        );

        return target_path;
    }

    panic!("Missing required environment variable for integration tests: ONEFUZZ_TEST_LIBFUZZER_TARGET");
}

async fn create_test_directory(config: &Path, target_exe: &Path) {}

struct TestLayout {
    // config, target, inputs, etc.
}
