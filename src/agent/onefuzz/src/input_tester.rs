// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::len_zero)]

use crate::{
    asan::{add_asan_log_env, check_asan_path, check_asan_string},
    env::{get_path_with_directory, update_path, LD_LIBRARY_PATH, PATH},
    expand::Expand,
    process::run_cmd,
};
use anyhow::{Error, Result};
#[cfg(target_os = "linux")]
use nix::sys::signal::{kill, Signal};
use stacktrace_parser::CrashLog;
#[cfg(any(target_os = "linux", target_family = "windows"))]
use stacktrace_parser::StackEntry;
#[cfg(target_os = "linux")]
use std::process::Stdio;
use std::{collections::HashMap, path::Path, time::Duration};
use tempfile::tempdir;

const DEFAULT_TIMEOUT: Duration = Duration::from_secs(5);
#[cfg(any(target_os = "linux", target_family = "windows"))]
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
    add_setup_to_ld_library_path: bool,
    add_setup_to_path: bool,
}

#[derive(Debug)]
pub struct Crash {
    pub call_stack: Vec<String>,
    pub crash_type: String,
    pub crash_site: String,
}

#[derive(Debug)]
pub struct TestResult {
    pub crash_log: Option<CrashLog>,
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
            check_debugger: false,
            check_retry_count: 0,
            add_setup_to_ld_library_path: false,
            add_setup_to_path: false,
        }
    }

    pub fn timeout(self, value: u64) -> Self {
        Self {
            timeout: Duration::from_secs(value),
            ..self
        }
    }

    pub fn check_asan_log(self, value: bool) -> Self {
        Self {
            check_asan_log: value,
            ..self
        }
    }

    pub fn check_asan_stderr(self, value: bool) -> Self {
        Self {
            check_asan_stderr: value,
            ..self
        }
    }

    pub fn check_debugger(self, value: bool) -> Self {
        Self {
            check_debugger: value,
            ..self
        }
    }

    pub fn check_retry_count(self, value: u64) -> Self {
        Self {
            check_retry_count: value,
            ..self
        }
    }

    pub fn add_setup_to_ld_library_path(self, value: bool) -> Self {
        Self {
            add_setup_to_ld_library_path: value,
            ..self
        }
    }

    pub fn add_setup_to_path(self, value: bool) -> Self {
        Self {
            add_setup_to_path: value,
            ..self
        }
    }

    pub fn set_optional<T>(self, value: Option<T>, setter: impl FnOnce(Self, T) -> Self) -> Self {
        if let Some(value) = value {
            setter(self, value)
        } else {
            self
        }
    }

    #[cfg(target_family = "windows")]
    async fn test_input_debugger(
        &self,
        argv: Vec<String>,
        env: HashMap<String, String>,
    ) -> Result<Option<CrashLog>> {
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
                .map(|f| match &f {
                    debugger::stack::DebugStackFrame::CorruptFrame => StackEntry {
                        line: f.to_string(),
                        ..Default::default()
                    },
                    debugger::stack::DebugStackFrame::Frame {
                        module_name,
                        module_offset,
                        symbol,
                        file_info,
                    } => StackEntry {
                        line: f.to_string(),
                        function_name: symbol.as_ref().map(|x| x.symbol().to_owned()),
                        function_offset: symbol.as_ref().map(|x| x.displacement()),
                        address: None,
                        module_offset: Some(*module_offset),
                        module_path: Some(module_name.to_owned()),
                        source_file_line: file_info.as_ref().map(|x| x.line.into()),
                        source_file_name: file_info
                            .as_ref()
                            .map(|x| x.file.rsplit_terminator('\\').next().map(|x| x.to_owned()))
                            .flatten(),
                        source_file_path: file_info.as_ref().map(|x| x.file.to_string()),
                    },
                })
                .collect();

            let crash_site = if let Some(frame) = call_stack.get(0) {
                frame.line.to_owned()
            } else {
                CRASH_SITE_UNAVAILABLE.to_owned()
            };

            let fault_type = exception.description.to_string();
            let sanitizer = fault_type.to_string();
            let summary = crash_site;

            Some(CrashLog::new(
                None, summary, sanitizer, fault_type, None, None, call_stack,
            )?)
        } else {
            None
        };

        Ok(crash)
    }

    #[cfg(target_os = "macos")]
    async fn test_input_debugger(
        &self,
        _args: Vec<String>,
        _env: HashMap<String, String>,
    ) -> Result<Option<CrashLog>> {
        bail!("running application under a debugger is not supported on macOS");
    }

    #[cfg(target_os = "linux")]
    async fn test_input_debugger(
        &self,
        args: Vec<String>,
        env: HashMap<String, String>,
    ) -> Result<Option<CrashLog>> {
        let mut cmd = std::process::Command::new(self.exe_path);
        cmd.args(args).stdin(Stdio::null());
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
                    .map(|(idx, frame)| StackEntry {
                        line: format!("#{} {}", idx, frame),
                        address: Some(frame.addr.0),
                        function_name: frame.function.as_ref().map(|x| x.name.clone()),
                        function_offset: frame.function.as_ref().map(|x| x.offset),
                        module_path: frame.module.as_ref().map(|x| x.name.clone()),
                        module_offset: frame.module.as_ref().map(|x| x.offset),
                        source_file_name: None,
                        source_file_line: None,
                        source_file_path: None,
                    })
                    .collect();

                let crash_type = crash.signal.to_string();

                let crash_site = if let Some(frame) = crash_thread.callstack.get(0) {
                    frame.to_string()
                } else {
                    CRASH_SITE_UNAVAILABLE.to_owned()
                };

                let summary = crash_site;
                let sanitizer = crash_type.clone();
                let fault_type = crash_type;

                Some(CrashLog::new(
                    None, summary, sanitizer, fault_type, None, None, call_stack,
                )?)
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

            let setup_dir = &self.setup_dir.to_path_buf();
            if self.add_setup_to_path {
                let new_path = match env.get(PATH) {
                    Some(v) => update_path(v.clone().into(), &setup_dir)?,
                    None => get_path_with_directory(PATH, &setup_dir)?,
                };
                env.insert(PATH.to_string(), new_path.to_string_lossy().to_string());
            }
            if self.add_setup_to_ld_library_path {
                let new_path = match env.get(LD_LIBRARY_PATH) {
                    Some(v) => update_path(v.clone().into(), &setup_dir)?,
                    None => get_path_with_directory(LD_LIBRARY_PATH, &setup_dir)?,
                };
                env.insert(
                    LD_LIBRARY_PATH.to_string(),
                    new_path.to_string_lossy().to_string(),
                );
            }

            if let Some(asan_dir) = &asan_dir {
                add_asan_log_env(&mut env, asan_dir.path());
            }

            (argv, env)
        };

        let mut error = None;
        let mut crash_log = None;

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

            crash_log = result.0;
            error = result.1;
            let output = result.2;

            // order of operations for checking for crashes:
            // 1. if we ran under a debugger, and that caught a crash
            // 2. if we have an ASAN log in our temp directory
            // 3. if we have an ASAN log to STDERR
            if crash_log.is_none() {
                crash_log = if let Some(asan_dir) = &asan_dir {
                    check_asan_path(asan_dir.path()).await?
                } else {
                    None
                };
            }

            if crash_log.is_none() && self.check_asan_stderr {
                if let Some(output) = output {
                    crash_log = check_asan_string(output.stderr).await?;
                }
            }

            if crash_log.is_some() {
                break;
            }
        }

        Ok(TestResult { crash_log, error })
    }

    pub async fn is_crash(&self, input_file: impl AsRef<Path>) -> Result<bool> {
        let test_result = self.test_input(input_file).await?;
        Ok(test_result.crash_log.is_some())
    }
}
