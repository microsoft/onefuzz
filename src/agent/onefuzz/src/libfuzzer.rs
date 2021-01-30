// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    expand::Expand,
    input_tester::{TestResult, Tester},
};
use anyhow::{Context, Result};
use std::{
    collections::HashMap,
    ffi::OsString,
    path::{Path, PathBuf},
    process::Stdio,
};
use tokio::process::{Child, Command};

const DEFAULT_MAX_TOTAL_SECONDS: i32 = 10 * 60;

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

    pub async fn check_help(&self) -> Result<()> {
        // Verify -help=1 exits with a zero return code, which validates the
        // libfuzzer works as we expect.
        let mut cmd = Command::new(&self.exe);

        cmd.kill_on_drop(true)
            .env_remove("RUST_LOG")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .arg("-help=1");

        let mut expand = Expand::new();
        expand
            .target_exe(&self.exe)
            .target_options(&self.options)
            .setup_dir(&self.setup_dir);

        for (k, v) in self.env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Pass custom option arguments.
        for o in expand.evaluate(self.options)? {
            cmd.arg(o);
        }

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
        let corpus_dir = corpus_dir.as_ref();
        let fault_dir = fault_dir.as_ref();

        let mut expand = Expand::new();
        expand
            .target_exe(&self.exe)
            .target_options(&self.options)
            .input_corpus(&corpus_dir)
            .crashes(&fault_dir)
            .setup_dir(&self.setup_dir);

        let mut cmd = Command::new(&self.exe);
        cmd.kill_on_drop(true)
            .env_remove("RUST_LOG")
            .stdout(Stdio::null())
            .stderr(Stdio::piped());

        // Set the environment.
        for (k, v) in self.env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Pass custom option arguments.
        for o in expand.evaluate(self.options)? {
            cmd.arg(o);
        }

        // check if a max_time is already set
        if self
            .options
            .iter()
            .find(|o| o.starts_with("-max_total_time"))
            .is_none()
        {
            cmd.arg(format!("-max_total_time={}", DEFAULT_MAX_TOTAL_SECONDS));
        }

        // When writing a new faulting input, the libFuzzer runtime _exactly_
        // prepends the value of `-artifact_prefix` to the new file name. To
        // specify that a new file `crash-<digest>` should be written to a
        // _directory_ `<corpus_dir>`, we must ensure that the prefix includes a
        // trailing path separator.
        let artifact_prefix: OsString = format!("-artifact_prefix={}/", fault_dir.display()).into();

        cmd.arg(&artifact_prefix);

        // Force a single worker, so we can manage workers ourselves.
        cmd.arg("-workers=1");

        // Set the read/written main corpus directory.
        cmd.arg(corpus_dir);

        // Set extra corpus directories that will be periodically rescanned.
        for dir in extra_corpus_dirs {
            cmd.arg(dir.as_ref());
        }

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

        let mut tester = Tester::new(&self.setup_dir, &self.exe, &options, &self.env);
        tester.check_asan_stderr(true).check_retry_count(retry);
        if let Some(timeout) = timeout {
            tester.timeout(timeout);
        }
        tester.test_input(test_input.as_ref()).await
    }

    pub async fn merge(
        &self,
        corpus_dir: impl AsRef<Path>,
        corpus_dirs: &[impl AsRef<Path>],
    ) -> Result<LibFuzzerMergeOutput> {
        let mut expand = Expand::new();
        expand
            .target_exe(&self.exe)
            .target_options(&self.options)
            .input_corpus(&corpus_dir)
            .setup_dir(&self.setup_dir);

        let mut cmd = Command::new(&self.exe);

        cmd.kill_on_drop(true)
            .env_remove("RUST_LOG")
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .arg("-merge=1")
            .arg(corpus_dir.as_ref());

        for dir in corpus_dirs {
            cmd.arg(dir.as_ref());
        }

        // Set the environment.
        for (k, v) in self.env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Pass custom option arguments.
        for o in expand.evaluate(self.options)? {
            cmd.arg(o);
        }

        let output = cmd
            .spawn()
            .with_context(|| format_err!("libfuzzer failed to start: {}", self.exe.display()))?
            .wait_with_output()
            .await
            .with_context(|| format_err!("libfuzzer failed to run: {}", self.exe.display()))?;

        let output_text = String::from_utf8_lossy(&output.stderr);
        let pat = r"MERGE-OUTER: (\d+) new files with (\d+) new features added";
        let re = regex::Regex::new(pat).unwrap();
        match re.captures_iter(&output_text).next() {
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
        let re = regex::Regex::new(r"#(\d+)\s*(?:pulse|INITED|NEW|REDUCE).*exec/s: (\d+)")?;

        let caps = match re.captures(line) {
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
        assert_eq!(parsed.execs_sec(), 699050.0_f64);
    }
}
