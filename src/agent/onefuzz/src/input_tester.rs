// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::len_zero)]

use crate::{
    asan::{add_asan_log_env, check_asan_path, check_asan_string, AsanLog},
    expand::Expand,
    process::run_cmd,
};
use anyhow::{Error, Result};
use std::{collections::HashMap, path::Path, time::Duration};
use tempfile::tempdir;

const DEFAULT_TIMEOUT: Duration = Duration::from_secs(5);
const CRASH_SITE_UNAVAILABLE: &str = "<crash site unavailable>";

pub struct Tester<'a> {
    setup_dir: &'a Path,
    exe_path: &'a Path,
    arguments: &'a [String],
    environ: &'a HashMap<String, String>,
    timeout: Duration,
    check_asan_log: bool,
    check_asan_stderr: bool,
    check_debugger: bool,
    check_retry_count: u64,
}

#[derive(Debug)]
pub struct Crash {
    pub call_stack: Vec<String>,
    pub crash_type: String,
    pub crash_site: String,
}

#[derive(Debug)]
pub struct TestResult {
    pub crash: Option<Crash>,
    pub asan_log: Option<AsanLog>,
    pub error: Option<Error>,
}

impl<'a> Tester<'a> {
    pub fn new(
        setup_dir: &'a Path,
        exe_path: &'a Path,
        arguments: &'a [String],
        environ: &'a HashMap<String, String>,
    ) -> Self {
        Self {
            setup_dir,
            exe_path,
            arguments,
            environ,
            timeout: DEFAULT_TIMEOUT,
            check_asan_log: false,
            check_asan_stderr: false,
            check_debugger: true,
            check_retry_count: 0,
        }
    }

    pub fn timeout(&mut self, value: u64) -> &mut Self {
        self.timeout = Duration::from_secs(value);
        self
    }

    pub fn check_asan_log(&mut self, value: bool) -> &mut Self {
        self.check_asan_log = value;
        self
    }

    pub fn check_asan_stderr(&mut self, value: bool) -> &mut Self {
        self.check_asan_stderr = value;
        self
    }

    pub fn check_debugger(&mut self, value: bool) -> &mut Self {
        self.check_debugger = value;
        self
    }

    pub fn check_retry_count(&mut self, value: u64) -> &mut Self {
        self.check_retry_count = value;
        self
    }

    #[cfg(target_os = "windows")]
    async fn test_input_debugger(
        &self,
        argv: Vec<String>,
        env: HashMap<String, String>,
    ) -> Result<Option<Crash>> {
        const IGNORE_FIRST_CHANCE_EXCEPTIONS: bool = true;
        let report = input_tester::crash_detector::test_process(
            self.exe_path,
            &argv,
            &env,
            self.timeout,
            IGNORE_FIRST_CHANCE_EXCEPTIONS,
            None,
        )?;

        let crash = if let Some(exception) = report.exceptions.last() {
            let call_stack: Vec<_> = exception
                .stack_frames
                .iter()
                .map(|f| f.to_string())
                .collect();

            let crash_site = if let Some(frame) = call_stack.get(0) {
                frame.to_string()
            } else {
                CRASH_SITE_UNAVAILABLE.to_owned()
            };

            let crash_type = exception.description.to_string();

            Some(Crash {
                call_stack,
                crash_type,
                crash_site,
            })
        } else {
            bail!("{}", report.exit_status);
        };

        Ok(crash)
    }

    #[cfg(target_os = "linux")]
    async fn test_input_debugger(
        &self,
        args: Vec<String>,
        env: HashMap<String, String>,
    ) -> Result<Option<Crash>> {
        let mut cmd = std::process::Command::new(self.exe_path);
        cmd.args(args);
        cmd.envs(&env);

        let (sender, receiver) = std::sync::mpsc::channel();

        // Create two async tasks: one off-thread task for the blocking triage run,
        // and one task that will kill the triage target if we time out.
        let triage = tokio::task::spawn_blocking(move || {
            // Spawn a triage run, but stop it before execing.
            //
            // This calls a blocking `wait()` internally, on the forked child.
            let triage = crate::triage::TriageCommand::new(cmd)?;

            // Share the new child ID with main thread.
            sender.send(triage.pid())?;

            // The target run is blocking, and may hang.
            triage.run()
        });

        // Save the new process ID of the spawned triage target, so we can try to kill
        // the (possibly hung) target out-of-band, if we time out.
        let target_pid = receiver.recv()?;

        let timeout = tokio::time::timeout(self.timeout, triage).await;
        let crash = if timeout.is_err() {
            use nix::sys::signal::{kill, Signal};
            // Yes. Try to kill the target process, if hung.
            kill(target_pid, Signal::SIGKILL)?;
            bail!("process timed out");
        } else {
            let report = timeout???;

            if let Some(crash) = report.crashes.last() {
                let crash_thread = crash
                    .threads
                    .get(&crash.tid.as_raw())
                    .ok_or_else(|| anyhow!("no thread info for crash thread ID = {}", crash.tid))?;

                let call_stack: Vec<_> = crash_thread
                    .callstack
                    .iter()
                    .enumerate()
                    .map(|(idx, frame)| format!("#{} {}", idx, frame))
                    .collect();

                let crash_type = crash.signal.to_string();

                let crash_site = if let Some(frame) = crash_thread.callstack.get(0) {
                    frame.to_string()
                } else {
                    CRASH_SITE_UNAVAILABLE.to_owned()
                };

                Some(Crash {
                    call_stack,
                    crash_type,
                    crash_site,
                })
            } else {
                None
            }
        };

        Ok(crash)
    }

    pub async fn test_input(&self, input_file: impl AsRef<Path>) -> Result<TestResult> {
        let asan_dir = if self.check_asan_log {
            Some(tempdir()?)
        } else {
            None
        };

        let (argv, env) = {
            let expand = Expand::new()
                .input_path(input_file)
                .target_exe(&self.exe_path)
                .target_options(&self.arguments)
                .setup_dir(&self.setup_dir);

            let argv = expand.evaluate(&self.arguments)?;
            let mut env: HashMap<String, String> = HashMap::new();
            for (k, v) in self.environ {
                env.insert(k.clone(), expand.evaluate_value(v)?);
            }

            if let Some(asan_dir) = &asan_dir {
                add_asan_log_env(&mut env, asan_dir.path());
            }

            (argv, env)
        };

        let mut crash = None;
        let mut error = None;
        let mut asan_log = None;

        let attempts = 1 + self.check_retry_count;
        for _ in 0..attempts {
            let result = if self.check_debugger {
                match self.test_input_debugger(argv.clone(), env.clone()).await {
                    Ok(crash) => (crash, None, None),
                    Err(error) => (None, Some(error), None),
                }
            } else {
                match run_cmd(&self.exe_path, argv.clone(), &env, self.timeout).await {
                    Ok(output) => (None, None, Some(output)),
                    Err(error) => (None, Some(error), None),
                }
            };

            crash = result.0;
            error = result.1;
            let output = result.2;

            asan_log = if let Some(asan_dir) = &asan_dir {
                check_asan_path(asan_dir.path()).await?
            } else {
                None
            };

            if asan_log.is_none() && self.check_asan_stderr {
                if let Some(output) = output {
                    asan_log = check_asan_string(output.stderr).await?;
                }
            }

            if crash.is_some() || asan_log.is_some() {
                break;
            }
        }

        Ok(TestResult {
            crash,
            asan_log,
            error,
        })
    }

    pub async fn is_crash(&self, input_file: impl AsRef<Path>) -> Result<bool> {
        let test_result = self.test_input(input_file).await?;
        Ok(test_result.crash.is_some() || test_result.asan_log.is_some())
    }
}
