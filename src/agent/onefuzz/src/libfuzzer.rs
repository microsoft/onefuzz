// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    env::{get_path_with_directory, LD_LIBRARY_PATH, PATH},
    expand::Expand,
    fs::{list_files, write_file},
    input_tester::{TestResult, Tester},
    machine_id::MachineIdentity,
};
use anyhow::{Context, Result};
use rand::seq::SliceRandom;
use rand::thread_rng;
use std::{
    collections::HashMap,
    ffi::{OsStr, OsString},
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
use tempfile::tempdir;
use tokio::process::{Child, Command};

const DEFAULT_MAX_TOTAL_SECONDS: i32 = 10 * 60;

use lazy_static::lazy_static;

lazy_static! {
    static ref LIBFUZZERLINEREGEX: regex::Regex =
        regex::Regex::new(r"#(\d+)\s*(?:pulse|INITED|NEW|REDUCE).*exec/s: (\d+)").unwrap();
}

#[derive(Debug)]
pub struct LibFuzzerMergeOutput {
    pub added_files_count: i32,
    pub added_feature_count: i32,
}

pub struct LibFuzzer {
    setup_dir: PathBuf,
    extra_setup_dir: Option<PathBuf>,
    extra_output_dir: Option<PathBuf>,
    exe: PathBuf,
    options: Vec<String>,
    env: HashMap<String, String>,
    machine_identity: MachineIdentity,
}

impl LibFuzzer {
    pub fn new(
        exe: PathBuf,
        options: Vec<String>,
        env: HashMap<String, String>,
        setup_dir: PathBuf,
        extra_setup_dir: Option<PathBuf>,
        extra_output_dir: Option<PathBuf>,
        machine_identity: MachineIdentity,
    ) -> Self {
        Self {
            exe,
            options,
            env,
            setup_dir,
            extra_setup_dir,
            extra_output_dir,
            machine_identity,
        }
    }

    // Build an async `Command`.
    fn build_command(
        &self,
        fault_dir: Option<&Path>,
        corpus_dir: Option<&Path>,
        extra_corpus_dirs: Option<&[&Path]>,
        extra_args: Option<&[&OsStr]>,
        custom_arg_filter: Option<&dyn Fn(String) -> Option<String>>,
    ) -> Result<Command> {
        let std_cmd = self.build_std_command(
            fault_dir,
            corpus_dir,
            extra_corpus_dirs,
            extra_args,
            custom_arg_filter,
        )?;

        // Make async (turn into tokio::process::Command):
        let mut cmd = Command::from(std_cmd);

        // Terminate the process if the `Child` handle is dropped.
        cmd.kill_on_drop(true);

        Ok(cmd)
    }

    // Build a non-async `Command`.
    pub fn build_std_command(
        &self,
        fault_dir: Option<&Path>,
        corpus_dir: Option<&Path>,
        extra_corpus_dirs: Option<&[&Path]>,
        extra_args: Option<&[&OsStr]>,
        custom_arg_filter: Option<&dyn Fn(String) -> Option<String>>,
    ) -> Result<std::process::Command> {
        let mut cmd = std::process::Command::new(&self.exe);
        cmd.env(PATH, get_path_with_directory(PATH, &self.setup_dir)?)
            .env_remove("RUST_LOG")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .arg("-workers=1");

        if cfg!(target_family = "unix") {
            cmd.env(
                LD_LIBRARY_PATH,
                get_path_with_directory(LD_LIBRARY_PATH, &self.setup_dir)?,
            );
        }

        let expand = Expand::new(&self.machine_identity)
            .machine_id()
            .target_exe(&self.exe)
            .target_options(&self.options)
            .setup_dir(&self.setup_dir)
            .set_optional_ref(&self.extra_setup_dir, Expand::extra_setup_dir)
            .set_optional_ref(&self.extra_output_dir, Expand::extra_output_dir)
            .set_optional(corpus_dir, Expand::input_corpus)
            .set_optional(fault_dir, Expand::crashes);

        // Expand and set environment variables:
        for (k, v) in &self.env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Set the read/written main corpus directory:
        if let Some(corpus_dir) = corpus_dir {
            cmd.arg(corpus_dir);
        }

        // Set extra (readonly) corpus directories that will also be used:
        if let Some(extra_corpus_dirs) = extra_corpus_dirs {
            cmd.args(extra_corpus_dirs);
        }

        // Pass any extra arguments that we need; this is done in this function
        // rather than the caller so that they can come before any custom options
        // that might interfere (e.g. -help=1 must come before -ignore_remaining_args=1).
        if let Some(extra_args) = extra_args {
            cmd.args(extra_args);
        }

        // Check if a max time is already set by the custom options, and set if not:
        if !self
            .options
            .iter()
            .any(|o| o.starts_with("-max_total_time"))
        {
            cmd.arg(format!("-max_total_time={DEFAULT_MAX_TOTAL_SECONDS}"));
        }

        // Pass custom option arguments last, to lessen the chance that they
        // interfere with standard options (e.g. use of -ignore_remaining_args=1),
        // and also to allow last-one-wins overriding if needed.
        //
        // We also allow filtering out parameters as well:
        cmd.args(
            expand
                .evaluate(&self.options)?
                .into_iter()
                .filter_map(custom_arg_filter.unwrap_or(&Some)),
        );

        Ok(cmd)
    }

    pub(crate) async fn verify_once(
        &self,
        check_fuzzer_help: bool,
        inputs: &[&Path],
    ) -> Result<()> {
        if check_fuzzer_help {
            self.check_help().await?;
        }

        let mut seen_inputs = false;

        // check 5 files at random from each input directory
        for input_dir in inputs {
            if tokio::fs::metadata(&input_dir).await.is_ok() {
                let mut files = list_files(&input_dir).await?;
                {
                    let mut rng = thread_rng();
                    files.shuffle(&mut rng);
                }

                for file in files.iter().take(5) {
                    self.check_input(file)
                        .await
                        .with_context(|| format!("checking input corpus: {}", file.display()))?;
                    seen_inputs = true;
                }
            } else {
                debug!("input dir doesn't exist: {input_dir:?}");
            }
        }

        if !seen_inputs {
            let temp_dir = tempdir()?;
            let empty = temp_dir.path().join("empty-file.txt");
            write_file(&empty, "").await?;
            self.check_input(&empty)
                .await
                .context("checking libFuzzer with empty input")?;
        }

        Ok(())
    }

    pub async fn verify(&self, check_fuzzer_help: bool, inputs: Option<&[&Path]>) -> Result<()> {
        // we’ve seen issues where executables cannot run on the first attempt;
        // for example, executables that depend upon an override in KnownDlls from taking effect
        //
        // so, we let the verification step fail for a while before we commit to its total failure
        const SLEEP_TIME: Duration = Duration::from_secs(5);
        const MAX_ATTEMPTS: usize = 20; // have seen this take 10+ attempts
        let mut attempts = 1;
        loop {
            let result = self
                .verify_once(check_fuzzer_help, inputs.unwrap_or_default())
                .await;

            match result {
                Ok(()) => return Ok(()),
                Err(e) => {
                    if attempts < MAX_ATTEMPTS {
                        warn!("libfuzzer verification failed, will retry: {e:?}");
                        tokio::time::sleep(SLEEP_TIME).await;
                    } else {
                        return Err(e.context(format!(
                            "libfuzzer verification still failing after {attempts} attempts"
                        )));
                    }
                }
            }

            attempts += 1;
        }
    }

    // Verify that the libfuzzer exits with a zero return code with a known
    // good input, which libfuzzer works as we expect.
    async fn check_input(&self, input: &Path) -> Result<()> {
        let mut cmd = self.build_command(
            None,
            None,
            None,
            // Custom args for this run: supply the required input. In this mode,
            // LibFuzzer will only execute one run of fuzzing unless overridden
            Some(&[input.as_ref()]),
            // Filter out any argument starting with `-runs=` from the custom
            // target options, if supplied, so that it doesn't make more than
            // one run happen:
            Some(&|arg: String| {
                if arg.starts_with("-runs=") {
                    None
                } else {
                    Some(arg)
                }
            }),
        )?;

        let result = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?
            .wait_with_output()
            .await
            .with_context(|| format_err!("libfuzzer failed to run: {}", self.exe.display()))?;

        if !result.status.success() {
            bail!(
                "libFuzzer failed when parsing an initial seed {:?}: stdout:{:?} stderr:{:?}",
                input.file_name().unwrap_or_else(|| input.as_ref()),
                String::from_utf8_lossy(&result.stdout),
                String::from_utf8_lossy(&result.stderr),
            );
        }

        Ok(())
    }

    /// Invoke `{target_exe} -help=1`. If this succeeds, then the dynamic linker is at
    /// least able to satisfy the fuzzer's shared library dependencies. User-authored
    /// dynamic loading may still fail later on, e.g. in `LLVMFuzzerInitialize()`.
    async fn check_help(&self) -> Result<()> {
        let mut cmd = self.build_command(None, None, None, Some(&["-help=1".as_ref()]), None)?;

        let result = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?
            .wait_with_output()
            .await
            .with_context(|| format_err!("libfuzzer failed to run: {}", self.exe.display()))?;

        if !result.status.success() {
            // To provide user-actionable errors, try to identify any missing shared libraries.
            match self.find_missing_libraries().await {
                Ok(missing) => {
                    if missing.is_empty() {
                        bail!("fuzzer does not respond to '-help=1'. no missing shared libraries detected. output: {:?}", result);
                    } else {
                        let missing = missing.join(", ");

                        bail!("fuzzer does not respond to '-help=1'. missing shared libraries: {}. output: {:?}", missing, result);
                    }
                }
                Err(err) => {
                    bail!("fuzzer does not respond to '-help=1'. additional error while checking for missing shared libraries: {}. output: {:?}", err, result);
                }
            }
        }

        Ok(())
    }

    async fn find_missing_libraries(&self) -> Result<Vec<String>> {
        let cmd = self.build_std_command(None, None, None, None, None)?;

        #[cfg(target_os = "linux")]
        let blocking = move || dynamic_library::linux::find_missing(cmd);

        #[cfg(target_os = "windows")]
        let blocking = move || dynamic_library::windows::find_missing(cmd);

        let missing = tokio::task::spawn_blocking(blocking).await??;
        let missing = missing.into_iter().map(|m| m.name).collect();

        Ok(missing)
    }

    pub async fn fuzz(
        &self,
        fault_dir: impl AsRef<Path>,
        corpus_dir: impl AsRef<Path>,
        extra_corpus_dirs: &[impl AsRef<Path>],
    ) -> Result<Child> {
        let extra_corpus_dirs: Vec<&Path> = extra_corpus_dirs.iter().map(|x| x.as_ref()).collect();

        // When writing a new faulting input, the libFuzzer runtime _exactly_
        // prepends the value of `-artifact_prefix` to the new file name. To
        // specify that a new file `crash-<digest>` should be written to a
        // _directory_ `<corpus_dir>`, we must ensure that the prefix includes a
        // trailing path separator.
        let artifact_prefix: OsString =
            format!("-artifact_prefix={}/", fault_dir.as_ref().display()).into();

        let mut cmd = self.build_command(
            Some(fault_dir.as_ref()),
            Some(corpus_dir.as_ref()),
            Some(&extra_corpus_dirs),
            Some(&[&artifact_prefix]),
            None,
        )?;

        let child = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?;

        Ok(child)
    }

    pub async fn repro(
        &self,
        test_input: impl AsRef<Path>,
        timeout: Option<u64>,
        retry: u64,
    ) -> Result<TestResult> {
        let mut options = self.options.clone();
        options.push("{input}".to_string());

        let mut tester = Tester::new(
            &self.setup_dir,
            self.extra_setup_dir.as_deref(),
            &self.exe,
            &options,
            &self.env,
            self.machine_identity.clone(),
        )
        .check_asan_stderr(true)
        .check_retry_count(retry)
        .add_setup_to_path(true)
        .set_optional(timeout, Tester::timeout);

        if cfg!(target_family = "unix") {
            tester = tester.add_setup_to_ld_library_path(true);
        }

        tester.test_input(test_input).await
    }

    pub async fn merge(
        &self,
        corpus_dir: impl AsRef<Path>,
        extra_corpus_dirs: &[impl AsRef<Path>],
    ) -> Result<LibFuzzerMergeOutput> {
        let extra_corpus_dirs: Vec<&Path> = extra_corpus_dirs.iter().map(|x| x.as_ref()).collect();
        let mut cmd = self.build_command(
            None,
            Some(corpus_dir.as_ref()),
            Some(&extra_corpus_dirs),
            Some(&["-merge=1".as_ref()]),
            None,
        )?;

        let output = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?
            .wait_with_output()
            .await
            .with_context(|| format_err!("libfuzzer failed to run: {}", self.exe.display()))?;

        let output_text = String::from_utf8_lossy(&output.stderr);
        let pat = r"MERGE-OUTER: (\d+) new files with (\d+) new features added";
        let re = regex::Regex::new(pat).unwrap();
        let captures = re.captures_iter(&output_text).next();
        match captures {
            Some(captures) => {
                let added_files_count = captures.get(1).unwrap().as_str().parse::<i32>().unwrap();
                let added_feature_count = captures.get(2).unwrap().as_str().parse::<i32>().unwrap();
                Ok(LibFuzzerMergeOutput {
                    added_files_count,
                    added_feature_count,
                })
            }
            None => Ok(LibFuzzerMergeOutput {
                added_files_count: 0,
                added_feature_count: 0,
            }),
        }
    }
}

pub struct LibFuzzerLine {
    _line: String,
    iters: u64,
    execs_sec: f64,
}

impl LibFuzzerLine {
    pub fn new(line: String, iters: u64, execs_sec: f64) -> Self {
        Self {
            iters,
            _line: line,
            execs_sec,
        }
    }

    pub fn parse(line: &str) -> Result<Option<Self>> {
        let caps = match LIBFUZZERLINEREGEX.captures(line) {
            Some(caps) => caps,
            None => return Ok(None),
        };

        let iters = caps[1].parse()?;
        let execs_sec = caps[2].parse()?;

        Ok(Some(Self::new(line.to_string(), iters, execs_sec)))
    }

    pub fn iters(&self) -> u64 {
        self.iters
    }

    pub fn execs_sec(&self) -> f64 {
        self.execs_sec
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_libfuzzer_line_pulse() {
        let line = r"#2097152        pulse  cov: 11 ft: 11 corp: 6/21b lim: 4096 exec/s: 699050 rss: 562Mb";

        let parsed = LibFuzzerLine::parse(line)
            .expect("parse error")
            .expect("no captures");

        assert_eq!(parsed.iters(), 2097152);

        let expected: f64 = 699050.0;
        let execs_sec = parsed.execs_sec();
        assert!(execs_sec.is_finite());
        assert!((execs_sec - expected).abs() < f64::EPSILON);
    }

    #[tokio::test]
    #[cfg(target_family = "unix")]
    async fn verify_initial_inputs() -> Result<()> {
        let bad_bin = PathBuf::from("/bin/false");
        let good_bin = PathBuf::from("/bin/echo");
        let temp_setup_dir = tempdir()?;
        let options = vec![];
        let env = HashMap::new();

        let input_file = temp_setup_dir.path().join("input.txt");
        write_file(&input_file, "input").await?;

        let fuzzer = LibFuzzer::new(
            bad_bin,
            options.clone(),
            env.clone(),
            temp_setup_dir.path().to_owned(),
            None,
            None,
            MachineIdentity {
                machine_id: uuid::Uuid::new_v4(),
                machine_name: "test-input".into(),
                scaleset_name: None,
            },
        );

        // verify catching bad exits with -help=1
        assert!(
            fuzzer.verify_once(true, &[]).await.is_err(),
            "checking false with -help=1"
        );

        // verify catching bad exits with inputs
        assert!(
            fuzzer
                .verify_once(false, &[temp_setup_dir.path()])
                .await
                .is_err(),
            "checking false with basic input"
        );

        // verify catching bad exits with no inputs
        assert!(
            fuzzer.verify_once(false, &[]).await.is_err(),
            "checking false without inputs"
        );

        let fuzzer = LibFuzzer::new(
            good_bin,
            options.clone(),
            env.clone(),
            temp_setup_dir.path().to_owned(),
            None,
            None,
            MachineIdentity {
                machine_id: uuid::Uuid::new_v4(),
                machine_name: "test-input".into(),
                scaleset_name: None,
            },
        );
        // verify good exits with -help=1
        assert!(
            fuzzer.verify_once(true, &[]).await.is_ok(),
            "checking true with -help=1"
        );

        // verify good exits with inputs
        assert!(
            fuzzer
                .verify_once(false, &[temp_setup_dir.path()])
                .await
                .is_ok(),
            "checking true with basic inputs"
        );

        // verify good exits with no inputs
        assert!(
            fuzzer.verify_once(false, &[]).await.is_ok(),
            "checking true without inputs"
        );

        Ok(())
    }
}
