use std::{
    env,
    path::{Path, PathBuf},
};

use tokio::fs;

use anyhow::Result;
use log::info;
use onefuzz_task_lib::local::template;
use std::time::Duration;
use tempfile::tempdir;
use tokio::time::timeout;

const TEMPLATES_PATH: &str = "./tests/templates";

#[tokio::test]
#[cfg_attr(not(feature = "integration_test"), ignore)]
async fn test_libfuzzer_basic_template() {
    let config = PathBuf::from(TEMPLATES_PATH).join("libfuzzer_basic.yml");
    let libfuzzer_target = get_libfuzzer_target();
    let test_layout = create_test_directory(&config, &libfuzzer_target)
        .await
        .expect("Failed to create test directory layout");

    info!("Running template for 1 minute...");
    if (timeout(
        Duration::from_secs(60),
        template::launch(&test_layout.config, None),
    )
    .await)
        .is_ok()
    {
        panic!("Execution terminated in less than a minute. Something went wrong")
    }

    verify_test_layout_structure_did_not_change(&test_layout).await;
    assert_directory_is_not_empty(&test_layout.crashes).await;
    assert_directory_is_not_empty(&test_layout.inputs).await;
    assert_directory_is_not_empty(&test_layout.regression_reports).await;
    verify_coverage_dir(&test_layout.coverage).await;
}

async fn verify_test_layout_structure_did_not_change(test_layout: &TestLayout) {
    assert_exists_and_is_dir(&test_layout.root).await;
    assert_exists_and_is_file(&test_layout.config).await;
    assert_exists_and_is_file(&test_layout.target_exe).await;
    assert_exists_and_is_dir(&test_layout.config).await;
    assert_exists_and_is_dir(&test_layout.crashdumps).await;
    assert_exists_and_is_dir(&test_layout.coverage).await;
    assert_exists_and_is_dir(&test_layout.crashes).await;
    assert_exists_and_is_dir(&test_layout.inputs).await;
    assert_exists_and_is_dir(&test_layout.regression_reports).await;
}

async fn verify_coverage_dir(coverage: &Path) {
    assert_directory_is_not_empty(coverage).await;

    let cobertura = PathBuf::from(coverage).join("cobertura-coverage.xml");
    assert_exists_and_is_file(&cobertura).await;
}

async fn assert_exists_and_is_dir(dir: &Path) {
    assert!(dir.exists(), "Expected directory to exist. dir = {:?}", dir);
    assert!(
        dir.is_dir(),
        "Expected path to be a directory. dir = {:?}",
        dir
    );
}

async fn assert_exists_and_is_file(file: &Path) {
    assert!(file.exists(), "Expected file to exist. file = {:?}", file);
    assert!(
        file.is_dir(),
        "Expected path to be a file. file = {:?}",
        file
    );
}

async fn assert_directory_is_not_empty(dir: &Path) {
    assert!(
        fs::read_dir(dir)
            .await
            .unwrap_or_else(|_| panic!("Failed to list files in directory. dir = {:?}", dir))
            .next_entry()
            .await
            .unwrap_or_else(|_| panic!(
                "Failed to get next file in directory listing. dir = {:?}",
                dir
            ))
            .is_some(),
        "Expected directory to not be empty. dir = {:?}",
        dir
    );
}

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

async fn create_test_directory(config: &Path, target_exe: &Path) -> Result<TestLayout> {
    let test_directory = tempdir().expect("Failed to create temporary directory");

    let inputs_directory = PathBuf::from(test_directory.path()).join("inputs");
    fs::create_dir(&inputs_directory).await?;

    let crashes_directory = PathBuf::from(test_directory.path()).join("crashes");
    fs::create_dir(&crashes_directory).await?;

    let crashdumps_directory = PathBuf::from(test_directory.path()).join("crashdumps");
    fs::create_dir(&crashdumps_directory).await?;

    let coverage_directory = PathBuf::from(test_directory.path()).join("coverage");
    fs::create_dir(&coverage_directory).await?;

    let regression_reports_directory =
        PathBuf::from(test_directory.path()).join("regression_reports");
    fs::create_dir(&regression_reports_directory).await?;

    let config_in_test =
        PathBuf::from(test_directory.path()).join(config.file_name().unwrap_or_else(|| {
            panic!("Failed to get file name for config. config = {:?}", config)
        }));
    fs::copy(config, &config_in_test).await?;

    let target_in_test =
        PathBuf::from(test_directory.path()).join(target_exe.file_name().unwrap_or_else(|| {
            panic!(
                "Failed to get file name for target_exe. target_exe = {:?}",
                target_exe
            )
        }));
    fs::copy(target_exe, &target_in_test).await?;

    Ok(TestLayout {
        root: PathBuf::from(test_directory.path()),
        config: config_in_test,
        target_exe: target_in_test,
        inputs: inputs_directory,
        crashes: crashes_directory,
        crashdumps: crashdumps_directory,
        coverage: coverage_directory,
        regression_reports: regression_reports_directory,
    })
}

struct TestLayout {
    root: PathBuf,
    config: PathBuf,
    target_exe: PathBuf,
    inputs: PathBuf,
    crashes: PathBuf,
    crashdumps: PathBuf,
    coverage: PathBuf,
    regression_reports: PathBuf,
}
