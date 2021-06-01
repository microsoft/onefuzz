// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::too_many_arguments)]
#![allow(clippy::implicit_hasher)]
#![cfg(windows)]

#[allow(unused)]
pub mod appverifier;
pub mod crash_detector;
pub mod logging;
pub mod summary;
pub mod test_result;
pub mod tester;

pub use tester::Tester;

use std::collections::HashMap;
use std::path::PathBuf;

use anyhow::Result;
use log::info;

use crate::appverifier::AppVerifierState;

/// Run the test file task in standalone mode where the input file is specified
/// from the command line.
pub fn run(
    output_dir: PathBuf,
    driver: PathBuf,
    driver_env: HashMap<String, String>,
    driver_args: Vec<String>,
    max_run_s: u64,
    ignore_first_chance_exceptions: bool,
    app_verifier_tests: Option<Vec<String>>,
    input: PathBuf,
    max_cores: Option<usize>,
) -> Result<()> {
    let tester = Tester::new(
        &output_dir,
        driver,
        driver_env,
        driver_args,
        max_run_s,
        ignore_first_chance_exceptions,
        app_verifier_tests,
        coverage::cache::ModuleCache::default(),
    )?;

    tester.set_appverifier(AppVerifierState::Enabled)?;

    let (summary, _results) = if std::fs::metadata(&input)?.is_file() {
        tester.test_single_file(&input)?
    } else {
        tester.test_dir(&input, max_cores)?
    };

    info!(
        "Test results summary: Crashes={} Passes={} HandledExceptions={} Timeouts={}",
        summary.crashes(),
        summary.passes(),
        summary.handled_exceptions(),
        summary.timeouts(),
    );

    Ok(())
}
