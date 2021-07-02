// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    env::{get_path_with_directory, LD_LIBRARY_PATH, PATH},
    expand::Expand,
    fs::{list_files, write_file},
    input_tester::{TestResult, Tester},
};
use anyhow::{Context, Result};
use rand::seq::SliceRandom;
use rand::thread_rng;
use std::{
    collections::HashMap,
    ffi::OsString,
    path::{Path, PathBuf},
    process::Stdio,
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

pub struct LibFuzzer<'a> {
    setup_dir: PathBuf,
    exe: PathBuf,
    options: &'a [String],
    env: &'a HashMap<String, String>,
}

impl<'a> LibFuzzer<'a> {
    pub fn new(
        exe: impl Into<PathBuf>,
        options: &'a [String],
        env: &'a HashMap<String, String>,
        setup_dir: impl Into<PathBuf>,
    ) -> Self {
        Self {
            exe: exe.into(),
            options,
            env,
            setup_dir: setup_dir.into(),
        }
    }

    fn build_command(
        &self,
        fault_dir: Option<&Path>,
        corpus_dir: Option<&Path>,
        extra_corpus_dirs: Option<&[&Path]>,
    ) -> Result<Command> {
        let mut cmd = Command::new(&self.exe);
        cmd.kill_on_drop(true)
            .env(PATH, get_path_with_directory(PATH, &self.setup_dir)?)
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

        let expand = Expand::new()
            .target_exe(&self.exe)
            .target_options(&self.options)
            .setup_dir(&self.setup_dir)
            .set_optional(corpus_dir, |tester, corpus_dir| {
                tester.input_corpus(&corpus_dir)
            })
            .set_optional(fault_dir, |tester, fault_dir| tester.crashes(&fault_dir));

        for (k, v) in self.env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Pass custom option arguments.
        for o in expand.evaluate(self.options)? {
            cmd.arg(o);
        }

        // Set the read/written main corpus directory.
        if let Some(corpus_dir) = corpus_dir {
            cmd.arg(corpus_dir);
        }

        // Set extra corpus directories that will be periodically rescanned.
        if let Some(extra_corpus_dirs) = extra_corpus_dirs {
            for dir in extra_corpus_dirs {
                cmd.arg(dir);
            }
        }

        // check if a max_time is already set
        if !self
            .options
            .iter()
            .any(|o| o.starts_with("-max_total_time"))
        {
            cmd.arg(format!("-max_total_time={}", DEFAULT_MAX_TOTAL_SECONDS));
        }

        Ok(cmd)
    }

    pub async fn verify(
        &self,
        check_fuzzer_help: bool,
        inputs: Option<Vec<PathBuf>>,
    ) -> Result<()> {
        if check_fuzzer_help {
            self.check_help().await?;
        }

        let mut seen_inputs = false;

        if let Some(inputs) = inputs {
            // check the 5 files at random from the input directories
            for input_dir in inputs {
                if tokio::fs::metadata(&input_dir).await.is_ok() {
                    let mut files = list_files(&input_dir).await?;
                    {
                        let mut rng = thread_rng();
                        files.shuffle(&mut rng);
                    }
                    for file in files.iter().take(5) {
                        self.check_input(&file).await.with_context(|| {
                            format!("checking input corpus: {}", file.display())
                        })?;
                        seen_inputs = true;
                    }
                } else {
                    println!("input dir doesn't exist: {:?}", input_dir);
                }
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

    async fn check_input(&self, input: &Path) -> Result<()> {
        // Verify that the libfuzzer exits with a zero return code with a known
        // good input, which libfuzzer works as we expect.

        let mut cmd = self.build_command(None, None, None)?;
        cmd.arg(&input);

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

    async fn check_help(&self) -> Result<()> {
        let mut cmd = self.build_command(None, None, None)?;
        cmd.arg("-help=1");

        let result = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?
            .wait_with_output()
            .await
            .with_context(|| format_err!("libfuzzer failed to run: {}", self.exe.display()))?;
        if !result.status.success() {
            bail!("fuzzer does not respond to '-help=1'. output:{:?}", result);
        }
        Ok(())
    }

    pub fn fuzz(
        &self,
        fault_dir: impl AsRef<Path>,
        corpus_dir: impl AsRef<Path>,
        extra_corpus_dirs: &[impl AsRef<Path>],
    ) -> Result<Child> {
        let extra_corpus_dirs: Vec<&Path> = extra_corpus_dirs.iter().map(|x| x.as_ref()).collect();
        let mut cmd = self.build_command(
            Some(fault_dir.as_ref()),
            Some(corpus_dir.as_ref()),
            Some(&extra_corpus_dirs),
        )?;

        // When writing a new faulting input, the libFuzzer runtime _exactly_
        // prepends the value of `-artifact_prefix` to the new file name. To
        // specify that a new file `crash-<digest>` should be written to a
        // _directory_ `<corpus_dir>`, we must ensure that the prefix includes a
        // trailing path separator.
        let artifact_prefix: OsString =
            format!("-artifact_prefix={}/", fault_dir.as_ref().display()).into();

        cmd.arg(&artifact_prefix);

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
        let mut options = self.options.to_owned();
        options.push("{input}".to_string());

        let mut tester = Tester::new(&self.setup_dir, &self.exe, &options, &self.env)
            .check_asan_stderr(true)
            .check_retry_count(retry)
            .add_setup_to_path(true)
            .set_optional(timeout, |tester, timeout| tester.timeout(timeout));

        if cfg!(target_family = "unix") {
            tester = tester.add_setup_to_ld_library_path(true);
        }

        tester.test_input(test_input.as_ref()).await
    }

    pub async fn merge(
        &self,
        corpus_dir: impl AsRef<Path>,
        extra_corpus_dirs: &[impl AsRef<Path>],
    ) -> Result<LibFuzzerMergeOutput> {
        let extra_corpus_dirs: Vec<&Path> = extra_corpus_dirs.iter().map(|x| x.as_ref()).collect();
        let mut cmd =
            self.build_command(None, Some(corpus_dir.as_ref()), Some(&extra_corpus_dirs))?;
        cmd.arg("-merge=1");

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

        let fuzzer = LibFuzzer::new(bad_bin, &options, &env, &temp_setup_dir.path());

        // verify catching bad exits with -help=1
        assert!(
            fuzzer.verify(true, None).await.is_err(),
            "checking false with -help=1"
        );

        // verify catching bad exits with inputs
        assert!(
            fuzzer
                .verify(false, Some(vec!(temp_setup_dir.path().to_path_buf())))
                .await
                .is_err(),
            "checking false with basic input"
        );

        // verify catching bad exits with no inputs
        assert!(
            fuzzer.verify(false, None).await.is_err(),
            "checking false without inputs"
        );

        let fuzzer = LibFuzzer::new(good_bin, &options, &env, &temp_setup_dir.path());
        // verify good exits with -help=1
        assert!(
            fuzzer.verify(true, None).await.is_ok(),
            "checking true with -help=1"
        );

        // verify good exits with inputs
        assert!(
            fuzzer
                .verify(false, Some(vec!(temp_setup_dir.path().to_path_buf())))
                .await
                .is_ok(),
            "checking true with basic inputs"
        );

        // verify good exits with no inputs
        assert!(
            fuzzer.verify(false, None).await.is_ok(),
            "checking true without inputs"
        );

        Ok(())
    }
}
