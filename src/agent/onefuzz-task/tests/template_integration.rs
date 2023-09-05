use std::{
    collections::HashSet,
    ffi::OsStr,
    path::{Path, PathBuf},
};

use tokio::fs;

use anyhow::Result;
use log::info;
use onefuzz_task_lib::local::template;
use std::time::Duration;
use tokio::time::timeout;

macro_rules! libfuzzer_tests {
    ($($name:ident: $value:expr,)*) => {
        $(
            #[tokio::test(flavor = "multi_thread")]
            #[cfg_attr(not(feature = "integration_test"), ignore)]
            async fn $name() {
                let _ = env_logger::builder().is_test(true).try_init();
                let (config, libfuzzer_target) = $value;
                test_libfuzzer_basic_template(PathBuf::from(config), PathBuf::from(libfuzzer_target)).await;
            }
        )*
    }
}

// This is the format for adding other templates/targets for this macro
// $TEST_NAME: ($RELATIVE_PATH_TO_TEMPLATE, $RELATIVE_PATH_TO_TARGET),
// Make sure that you place the target binary in CI
libfuzzer_tests! {
    libfuzzer_basic: ("./tests/templates/libfuzzer_basic.yml", "./tests/targets/simple/fuzz.exe"),
}

async fn test_libfuzzer_basic_template(config: PathBuf, libfuzzer_target: PathBuf) {
    assert_exists_and_is_file(&config).await;
    assert_exists_and_is_file(&libfuzzer_target).await;

    let test_layout = create_test_directory(&config, &libfuzzer_target)
        .await
        .expect("Failed to create test directory layout");

    info!("Executed test from: {:?}", &test_layout.root);
    info!("Running template for 3 minutes...");
    if let Ok(template_result) = timeout(
        Duration::from_secs(60),
        template::launch(&test_layout.config, None),
    )
    .await
    {
        // Something went wrong when running the template so lets print out the template to be helpful
        info!("Printing config as it was used in the test:");
        info!("{:?}", fs::read_to_string(&test_layout.config).await);
        template_result.unwrap();
    }

    verify_test_layout_structure_did_not_change(&test_layout).await;
    assert_directory_is_not_empty(&test_layout.crashes).await;
    assert_directory_is_not_empty(&test_layout.inputs).await;
    verify_coverage_dir(&test_layout.coverage).await;
}

async fn verify_test_layout_structure_did_not_change(test_layout: &TestLayout) {
    assert_exists_and_is_dir(&test_layout.root).await;
    assert_exists_and_is_file(&test_layout.config).await;
    assert_exists_and_is_file(&test_layout.target_exe).await;
    // assert_exists_and_is_dir(&test_layout.crashdumps).await;
    // assert_exists_and_is_dir(&test_layout.coverage).await;
    assert_exists_and_is_dir(&test_layout.crashes).await;
    assert_exists_and_is_dir(&test_layout.inputs).await;
    // assert_exists_and_is_dir(&test_layout.regression_reports).await;
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
        file.is_file(),
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

async fn create_test_directory(config: &Path, target_exe: &Path) -> Result<TestLayout> {
    let mut test_directory = PathBuf::from(".").join(uuid::Uuid::new_v4().to_string());
    fs::create_dir_all(&test_directory).await?;
    test_directory = test_directory.canonicalize()?;

    let mut inputs_directory = PathBuf::from(&test_directory).join("inputs");
    fs::create_dir(&inputs_directory).await?;
    inputs_directory = inputs_directory.canonicalize()?;

    let mut crashes_directory = PathBuf::from(&test_directory).join("crashes");
    fs::create_dir(&crashes_directory).await?;
    crashes_directory = crashes_directory.canonicalize()?;

    let mut crashdumps_directory = PathBuf::from(&test_directory).join("crashdumps");
    fs::create_dir(&crashdumps_directory).await?;
    crashdumps_directory = crashdumps_directory.canonicalize()?;

    let mut coverage_directory = PathBuf::from(&test_directory).join("coverage");
    fs::create_dir(&coverage_directory).await?;
    coverage_directory = coverage_directory.canonicalize()?;

    let mut regression_reports_directory =
        PathBuf::from(&test_directory).join("regression_reports");
    fs::create_dir(&regression_reports_directory).await?;
    regression_reports_directory = regression_reports_directory.canonicalize()?;

    let mut target_in_test = PathBuf::from(&test_directory).join("fuzz.exe");
    fs::copy(target_exe, &target_in_test).await?;
    target_in_test = target_in_test.canonicalize()?;

    let mut interesting_extensions = HashSet::new();
    interesting_extensions.insert(Some(OsStr::new("so")));
    interesting_extensions.insert(Some(OsStr::new("pdb")));
    let mut f = fs::read_dir(target_exe.parent().unwrap()).await?;
    while let Ok(Some(f)) = f.next_entry().await {
        if interesting_extensions.contains(&f.path().extension()) {
            fs::copy(f.path(), PathBuf::from(&test_directory).join(f.file_name())).await?;
        }
    }

    let mut config_data = fs::read_to_string(config).await?;

    config_data = config_data
        .replace("{TARGET_PATH}", target_in_test.to_str().unwrap())
        .replace("{INPUTS_PATH}", inputs_directory.to_str().unwrap())
        .replace("{CRASHES_PATH}", crashes_directory.to_str().unwrap())
        .replace("{CRASHDUMPS_PATH}", crashdumps_directory.to_str().unwrap())
        .replace("{COVERAGE_PATH}", coverage_directory.to_str().unwrap())
        .replace(
            "{REGRESSION_REPORTS_PATH}",
            regression_reports_directory.to_str().unwrap(),
        )
        .replace("{TEST_DIRECTORY}", test_directory.to_str().unwrap());

    let mut config_in_test =
        PathBuf::from(&test_directory).join(config.file_name().unwrap_or_else(|| {
            panic!("Failed to get file name for config. config = {:?}", config)
        }));

    fs::write(&config_in_test, &config_data).await?;
    config_in_test = config_in_test.canonicalize()?;

    Ok(TestLayout {
        root: test_directory,
        config: config_in_test,
        target_exe: target_in_test,
        inputs: inputs_directory,
        crashes: crashes_directory,
        crashdumps: crashdumps_directory,
        coverage: coverage_directory,
        regression_reports: regression_reports_directory,
    })
}

#[derive(Debug)]
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
