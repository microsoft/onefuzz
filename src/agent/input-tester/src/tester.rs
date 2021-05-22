// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::match_like_matches_macro)]
#![allow(clippy::single_component_path_imports)]
#![allow(clippy::option_map_or_none)]
#![allow(clippy::ptr_arg)]
#![allow(clippy::bind_instead_of_map)]
#![allow(clippy::len_zero)]

//! This module runs the application under a debugger to detect exceptions or time outs.
use std::{
    collections::HashMap,
    fs,
    io::Write,
    path::{Path, PathBuf},
    sync::{Arc, RwLock},
    time::Duration,
};

use anyhow::{Context, Result};
use coverage::cache::ModuleCache;
use log::{error, info, trace, warn};
use num_cpus;
use rayon::{prelude::*, ThreadPoolBuilder};
use sha2::{Digest, Sha256};

use crate::{
    appverifier::{self, AppVerifierController, AppVerifierState},
    crash_detector::{self, DebuggerResult},
    logging,
    summary::Summary,
    test_result::{new_test_result, Exception, ExceptionCode, ExceptionDescription, TestResult},
};

macro_rules! writecrlf {
    ($dst:expr) => (
        write!($dst, "\r\n")
    );
    ($dst:expr,) => (
        writecrlf!($dst)
    );
    ($dst:expr, $($arg:tt)*) => (
        write!($dst, $($arg)*).and_then(|_| writecrlf!($dst))
    );
}

const THREAD_POOL_NAME: &str = "input-tester";

const MAX_CRASH_SAMPLES: usize = 10;

pub struct InputTestResult {
    pub debugger_result: DebuggerResult,
    pub input_path: PathBuf,
}

impl InputTestResult {
    pub fn new(debugger_result: DebuggerResult, input_path: PathBuf) -> Self {
        InputTestResult {
            debugger_result,
            input_path,
        }
    }
}

pub struct Tester {
    driver: PathBuf,
    driver_env: HashMap<String, String>,
    driver_args: Vec<String>,
    max_run_s: u64,
    ignore_first_chance_exceptions: bool,
    appverif_controller: Option<AppVerifierController>,
    bugs_found_dir: PathBuf,
    module_cache: RwLock<ModuleCache>,
}

impl Tester {
    pub fn new(
        output_dir: &Path,
        driver: PathBuf,
        driver_env: HashMap<String, String>,
        driver_args: Vec<String>,
        max_run_s: u64,
        ignore_first_chance_exceptions: bool,
        app_verifier_tests: Option<Vec<String>>,
        module_cache: ModuleCache,
    ) -> Result<Arc<Self>> {
        let mut bugs_found_dir = output_dir.to_path_buf();
        bugs_found_dir.push("bugs_found");
        ensure_directory_exists(&bugs_found_dir)
            .with_context(|| format!("Creating directory {}", bugs_found_dir.display()))?;

        let appverif_controller = if let Some(app_verifier_tests) = app_verifier_tests {
            if let Some(exe_name) = Path::new(&driver).file_name() {
                let appverif_controller =
                    AppVerifierController::new(exe_name, &app_verifier_tests)?;

                let cloned_appverif = appverif_controller.clone();
                atexit::register(move || {
                    if let Err(e) = cloned_appverif.set(AppVerifierState::Disabled) {
                        error!("Error disabling appverif: {}", e);
                    }
                });

                Some(appverif_controller)
            } else {
                anyhow::bail!("Missing executable name in path {}", driver.display());
            }
        } else {
            None
        };

        Ok(Arc::new(Tester {
            appverif_controller,
            driver,
            driver_env,
            driver_args,
            max_run_s,
            ignore_first_chance_exceptions,
            bugs_found_dir,
            module_cache: RwLock::new(module_cache),
        }))
    }

    /// Run the test file task with the specified input file.
    pub fn test_application(&self, input_path: impl AsRef<Path>) -> Result<InputTestResult> {
        let app_args = args_with_input_file_applied(&self.driver_args, &input_path)?;

        let mut module_cache = self
            .module_cache
            .write()
            .map_err(|err| anyhow::format_err!("{:?}", err))?;

        crash_detector::test_process(
            &self.driver,
            &app_args,
            &self.driver_env,
            Duration::from_secs(self.max_run_s),
            self.ignore_first_chance_exceptions,
            Some(&mut *module_cache),
        )
        .and_then(|result| {
            let result = InputTestResult::new(result, PathBuf::from(input_path.as_ref()));
            log_input_test_result(&result);
            Ok(result)
        })
    }

    /// Test each file with the expected extension in the specified directory.
    /// Testing will be run in parallel unless the config specifies otherwise.
    pub fn test_dir(
        self: Arc<Self>,
        input_dir: impl AsRef<Path>,
        max_cores: Option<usize>,
    ) -> Result<(Summary, Vec<TestResult>)> {
        let threads = max_cores.unwrap_or_else(num_cpus::get);
        let threadpool = ThreadPoolBuilder::new()
            .thread_name(|idx| format!("{}-{}", THREAD_POOL_NAME, idx))
            .num_threads(threads)
            .build()?;

        let files_to_test: Vec<_> = fs::read_dir(&input_dir)
            .with_context(|| format!("Reading directory {}", input_dir.as_ref().display()))?
            .filter_map(|entry| match entry {
                Ok(item) => {
                    let path = item.path();
                    if path.is_file() {
                        Some(path)
                    } else {
                        None
                    }
                }
                Err(err) => {
                    warn!("Error reading directory entry: {}", err);
                    None
                }
            })
            .collect();

        let self_clone = Arc::clone(&self);
        Ok(threadpool.scope(|_s| {
            let results: Vec<InputTestResult> = files_to_test
                .par_iter()
                .map(move |input_path| self_clone.test_application(&input_path))
                .filter_map(|result| match result {
                    Ok(result) => Some(result),
                    Err(err) => {
                        error!("Debugger error: {}", err);
                        None
                    }
                })
                .collect();

            // Count the number of passes, crashes, etc.
            let mut summary = Summary::new();
            for result in &results {
                summary.update(&result.debugger_result);
            }

            // Copy failing inputs to the bugs_found directory and create a log for the crash,
            // and return a collection of results to possibly be reported.
            let mut test_results = vec![];
            for result in results {
                match self.prepare_test_result(&result) {
                    Ok(Some(result)) => test_results.push(result),
                    Ok(None) => {}
                    Err(e) => {
                        error!("Error reporting results: {}", e);
                    }
                }
            }

            (summary, test_results)
        }))
    }

    pub fn test_single_file(
        &self,
        input_path: impl AsRef<Path>,
    ) -> Result<(Summary, Vec<TestResult>)> {
        let test_result = self.test_application(input_path)?;

        let summary = Summary::from(&test_result.debugger_result);

        let mut results = vec![];
        if let Some(result) = self.prepare_test_result(&test_result)? {
            results.push(result);
        }

        Ok((summary, results))
    }

    fn is_bucket_full(&self, bucket_dir: &Path) -> Result<bool> {
        let dir_entries = fs::read_dir(bucket_dir)
            .with_context(|| format!("Reading directory {}", bucket_dir.display()))?;

        // We save the input and create a directory for the log+repro script,
        // so divide count of files by 2 to get number of files.
        Ok((dir_entries.count() / 2) >= MAX_CRASH_SAMPLES)
    }

    fn create_test_failure_artifacts(
        &self,
        log_dir: &Path,
        result: &InputTestResult,
        deduped_input_path: &Path,
    ) -> Result<()> {
        // Make a directory for our logs.
        ensure_directory_exists(&log_dir).context("Creating log directory for crash/timeout.")?;

        // Create a markdown file in our log directory.
        let stem = match result.input_path.file_stem() {
            Some(stem) => stem,
            None => anyhow::bail!(
                "Unexpected missing file stem {}",
                result.input_path.display()
            ),
        };
        let summary_path = log_dir.join(stem).with_extension("md");
        result
            .debugger_result
            .write_markdown_summary(&summary_path)
            .context("Writing markdown summary for crash/timeout.")?;

        // Create a batch file to help reproduce the bug with the settings we used.
        self.create_repro_bat(&log_dir, &result.input_path, &deduped_input_path)?;

        Ok(())
    }

    fn create_repro_bat(
        &self,
        log_dir: &Path,
        orig_input_path: &Path,
        deduped_input_path: &Path,
    ) -> Result<()> {
        let repro_bat_path = log_dir.join("repro.bat");
        let mut repro_bat = fs::File::create(&repro_bat_path)?;

        writecrlf!(repro_bat, "@echo off")?;

        if let Some(appverif) = &self.appverif_controller {
            write_appverif_calls(
                &mut repro_bat,
                appverif.appverif_path(),
                appverif.enable_command_lines(),
            )?;
            writecrlf!(repro_bat)?;
        }

        writecrlf!(
            repro_bat,
            "@rem Original input file tested was: {}",
            orig_input_path.display()
        )?;
        let app_args = args_with_input_file_applied(&self.driver_args, &deduped_input_path)?;
        writecrlf!(
            repro_bat,
            "{}",
            logging::command_invocation(&self.driver, &app_args[..])
        )?;

        if let Some(appverif) = &self.appverif_controller {
            write_appverif_calls(
                &mut repro_bat,
                appverif.appverif_path(),
                appverif.disable_command_lines(),
            )?;
            writecrlf!(repro_bat)?;
        }

        return Ok(());

        fn write_appverif_calls(
            repro_bat: &mut fs::File,
            appverif_path: &Path,
            args_with_comments: &[appverifier::ArgsWithComments],
        ) -> Result<()> {
            for args in args_with_comments {
                writecrlf!(repro_bat)?;
                for comment in args.comments {
                    write!(repro_bat, "@rem ")?;
                    writecrlf!(repro_bat, "{}", comment)?;
                }
                writecrlf!(
                    repro_bat,
                    "{}",
                    logging::command_invocation(appverif_path, &args.args)
                )?;
            }
            Ok(())
        }
    }

    pub fn prepare_test_result(&self, result: &InputTestResult) -> Result<Option<TestResult>> {
        if !result.debugger_result.any_crashes_or_timed_out() {
            return Ok(Some(new_test_result(
                result.debugger_result.clone(),
                &result.input_path,
                Path::new(""),
            )));
        }

        // We bucketize results into folders and limit the number of samples in each bucket
        // so we hopefully avoid filling up the disk.
        //
        // A single sample could live in multiple buckets, so we try to pick the most serious
        // and so the sample is stored just once.
        let bucket = if result.debugger_result.any_crashes() {
            most_serious_exception(&result.debugger_result)
                .stack_hash
                .to_string()
        } else {
            "timeout".to_owned()
        };
        let mut bucket_dir = self.bugs_found_dir.clone();
        bucket_dir.push(bucket);
        ensure_directory_exists(&bucket_dir)?;

        if self.is_bucket_full(&bucket_dir)? {
            warn!(
                "TestInput not copied, max results ({}) found in {}",
                MAX_CRASH_SAMPLES,
                bucket_dir.display(),
            );
            return Ok(None);
        }

        let copy_result =
            copy_input_file_result(&result.input_path, &bucket_dir).with_context(|| {
                format!(
                    "Copying input file `{}` to `{}`",
                    result.input_path.display(),
                    bucket_dir.display()
                )
            })?;

        match copy_result {
            None => {
                // We assume if we've previously seen an input (checked by hashing the input)
                // and it crashed again, there is no value in reporting it again even though
                // we may see different exceptions or stacks.
                Ok(None)
            }
            Some(copied_file) => {
                // Logs will go in a directory matching the copied input name w/o an extension.
                let mut logs_dir = PathBuf::from(&copied_file);
                logs_dir.set_file_name(format!(
                    "{}-logs",
                    logs_dir.file_stem().unwrap().to_str().unwrap()
                ));
                self.create_test_failure_artifacts(&logs_dir, &result, &copied_file)?;
                Ok(Some(new_test_result(
                    result.debugger_result.clone(),
                    &result.input_path,
                    &logs_dir,
                )))
            }
        }
    }

    pub fn use_appverifier(&self) -> bool {
        self.appverif_controller.is_some()
    }

    pub fn set_appverifier(&self, state: AppVerifierState) -> Result<()> {
        if let Some(appverif_controller) = &self.appverif_controller {
            appverif_controller
                .set(state)
                .with_context(|| format!("Setting appverifier to {:?}", state))?;
        }

        Ok(())
    }
}

fn exception_from_throw(exception: &Exception) -> bool {
    match exception.description {
        ExceptionDescription::GenericException(ExceptionCode::ClrException)
        | ExceptionDescription::GenericException(ExceptionCode::CppException) => true,
        _ => false,
    }
}

/// Using heuristics, choose the most serious exception. Typically this would be the one that
/// crashes the program, which normally would be the last exception we see.
fn most_serious_exception(result: &DebuggerResult) -> &Exception {
    for e in &result.exceptions {
        // An unhandled exception will cause a crash, so treat those as more serious
        // than any handled exception.
        //
        // I'm not sure it's possible to have more than one unhandled exception, but if it
        // was possible, we want the first, so we search forwards. If a second unhandled exception
        // is possible, I'd guess it comes from something like a vectored exception handler.
        if !e.first_chance {
            return e;
        }
    }

    // Every exception was handled, but we can assume some exceptions are less severe than
    // others. For starters, we'll assume any throw statement is less severe than a non-throw.
    let a_throw = result.exceptions.iter().find(|e| exception_from_throw(e));

    // If we have no throw, assume the first exception is the root cause to be reported.
    a_throw.unwrap_or(&result.exceptions[0])
}

/// Read the file and hash the file contents using sha2.
/// Returns the digest of the hash as a string in lowercase hex.
fn hash_file_contents(file: impl AsRef<Path>) -> Result<String> {
    let data = fs::read(file.as_ref())?;
    let digest = Sha256::digest(&data);
    Ok(hex::encode(&digest[..]))
}

/// Copy file to directory, but hash the file contents to use as the filename
/// so we can uniquely identify the input. This helps:
///   * Redundantly trying to repro an input that was previously observed.
///   * Avoid investigating identical inputs that result in different stack hashes.
///
/// If the target file exists, nothing is copied.
///
/// Returns the destination file name in a PathBuf if we've never seen the input file before.
fn copy_input_file_result(
    file: impl AsRef<Path>,
    directory: impl AsRef<Path>,
) -> Result<Option<PathBuf>> {
    let mut dest = directory.as_ref().to_path_buf();
    let hash = hash_file_contents(&file)?;
    dest.push(hash);
    if let Some(ext) = file.as_ref().extension() {
        dest.set_extension(ext);
    }

    if dest.is_dir() || dest.is_file() {
        warn!("Not reporting result: {} exists", dest.display());
        Ok(None)
    } else {
        fs::copy(file, &dest)?;
        Ok(Some(dest))
    }
}

fn log_input_test_result(result: &InputTestResult) {
    let debugger_result = &result.debugger_result;
    let input_path = &result.input_path;
    if debugger_result.exceptions.is_empty() {
        trace!("No bugs found in {}", input_path.display())
    } else {
        for exception in &debugger_result.exceptions {
            info!(
                "Exception found testing {} ExceptionCode=0x{:08x} Description={} FirstChance={} StackHash={}",
                input_path.display(),
                exception.exception_code,
                exception.description,
                exception.first_chance,
                exception.stack_hash,
            );
        }
    }
}

/// Replace `@@` with `input_file` in `args`.
pub fn args_with_input_file_applied(
    args: &Vec<impl AsRef<str>>,
    input_file: impl AsRef<Path>,
) -> Result<Vec<String>> {
    let mut result = vec![];

    let input_file = input_file.as_ref().to_str().ok_or_else(|| {
        anyhow::anyhow!(
            "unexpected unicode character in path {}",
            input_file.as_ref().display()
        )
    })?;
    for arg in args {
        let arg: String = arg.as_ref().replace("@@", input_file);
        result.push(arg);
    }

    Ok(result)
}

/// Create the specified directory if it does not already exist.
pub fn ensure_directory_exists(path: impl AsRef<Path>) -> Result<()> {
    let path = path.as_ref();
    if path.is_dir() {
        return Ok(());
    }

    // Either directory does not exist, or maybe it's a file, either way,
    // we'll try to create the directory and using that result for the error if any.
    fs::create_dir_all(&path).with_context(|| format!("Creating directory {}", path.display()))?;
    Ok(())
}
