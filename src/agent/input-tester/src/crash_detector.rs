// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! This module implements a simple debugger to detect exceptions in an application.
use std::{
    self,
    collections::HashMap,
    ffi::{OsStr, OsString},
    fs,
    io::Write,
    path::Path,
    process::{Command, Output, Stdio},
    time::{Duration, Instant},
};

use anyhow::Result;
use coverage::{
    block::{windows::Recorder as BlockCoverageRecorder, CommandBlockCov},
    cache::ModuleCache,
};
use debugger::{BreakpointId, DebugEventHandler, Debugger, ModuleLoadInfo};
use log::{debug, error, trace};
use win_util::{
    pipe_handle::{pipe, PipeReaderNonBlocking},
    process,
};
use winapi::{
    shared::minwindef::DWORD,
    um::{
        minwinbase::EXCEPTION_DEBUG_INFO,
        winnt::{DBG_EXCEPTION_NOT_HANDLED, HANDLE},
    },
};

use crate::{
    logging,
    test_result::{
        asan, new_exception,
        vcpp_debugger::{self, VcppDebuggerExceptionInfo},
        Exception, ExitStatus,
    },
};

#[derive(Clone)]
pub struct DebuggerResult {
    pub exceptions: Vec<Exception>,
    pub exit_status: ExitStatus,
    pub stdout: String,
    pub stderr: String,
    pub debugger_output: String,
    pub coverage: Option<CommandBlockCov>,
}

impl DebuggerResult {
    fn new(
        exceptions: Vec<Exception>,
        exit_status: ExitStatus,
        stdout: String,
        stderr: String,
        debugger_output: String,
        coverage: Option<CommandBlockCov>,
    ) -> Self {
        DebuggerResult {
            exceptions,
            exit_status,
            stdout,
            stderr,
            debugger_output,
            coverage,
        }
    }

    pub fn any_crashes(&self) -> bool {
        !self.exceptions.is_empty()
    }

    pub fn timed_out(&self) -> bool {
        self.exit_status.is_timeout()
    }

    pub fn any_crashes_or_timed_out(&self) -> bool {
        self.any_crashes() || self.timed_out()
    }

    pub fn write_markdown_summary(&self, summary_path: &Path) -> Result<()> {
        let mut file = fs::File::create(&summary_path)?;
        writeln!(file, "# Test Results")?;
        writeln!(file)?;
        writeln!(file, "## Output")?;
        writeln!(file)?;
        writeln!(file, "### Standard Output")?;
        writeln!(file)?;
        writeln!(file, "```")?;
        writeln!(file, "{}", self.stdout)?;
        writeln!(file, "```")?;
        writeln!(file)?;
        writeln!(file, "### Standard Error")?;
        writeln!(file)?;
        writeln!(file, "```")?;
        writeln!(file, "{}", self.stderr)?;
        writeln!(file, "```")?;
        writeln!(file)?;
        writeln!(file, "### Exit status")?;
        writeln!(file)?;
        writeln!(file, "{}", self.exit_status)?;
        writeln!(file)?;
        if !self.debugger_output.is_empty() {
            writeln!(file, "## Debugger output")?;
            writeln!(file)?;
            writeln!(file, "```")?;
            writeln!(file, "{}", self.debugger_output)?;
            writeln!(file, "```")?;
            writeln!(file)?;
        }
        writeln!(file, "## Exceptions")?;
        writeln!(file)?;
        for exception in &self.exceptions {
            writeln!(file)?;
            writeln!(file, "```")?;
            writeln!(file, "{}", exception)?;
            writeln!(file, "```")?;
        }
        writeln!(file)?;
        Ok(())
    }
}

struct CrashDetectorEventHandler<'a> {
    start_time: Instant,
    max_duration: Duration,
    ignore_first_chance_exceptions: bool,
    any_target_terminated: bool,
    timed_out: bool,
    stdout: PipeReaderNonBlocking,
    stderr: PipeReaderNonBlocking,
    stdout_buffer: Vec<u8>,
    stderr_buffer: Vec<u8>,
    debugger_output: String,
    exceptions: Vec<Exception>,
    coverage: Option<BlockCoverageRecorder<'a>>,
}

impl<'a> CrashDetectorEventHandler<'a> {
    pub fn new(
        stdout: PipeReaderNonBlocking,
        stderr: PipeReaderNonBlocking,
        ignore_first_chance_exceptions: bool,
        start_time: Instant,
        max_duration: Duration,
        coverage: Option<BlockCoverageRecorder<'a>>,
    ) -> Self {
        Self {
            start_time,
            max_duration,
            ignore_first_chance_exceptions,
            any_target_terminated: false,
            timed_out: false,
            stdout,
            stdout_buffer: vec![],
            stderr,
            stderr_buffer: vec![],
            debugger_output: String::new(),
            exceptions: vec![],
            coverage,
        }
    }
}

fn is_vcpp_notification(exception: &EXCEPTION_DEBUG_INFO, target_process_handle: HANDLE) -> bool {
    if exception.ExceptionRecord.ExceptionCode == vcpp_debugger::EXCEPTION_VISUALCPP_DEBUGGER {
        match VcppDebuggerExceptionInfo::from_exception_record(
            &exception.ExceptionRecord,
            !process::is_wow64_process(target_process_handle),
        ) {
            VcppDebuggerExceptionInfo::ThreadName(_) => {
                return true;
            }
            VcppDebuggerExceptionInfo::Probe(probe_info) => {
                if let Err(e) = probe_info.notify_target(target_process_handle) {
                    error!("Error notifying target on vcpp probe: {}", e);
                }
                return true;
            }
            VcppDebuggerExceptionInfo::Fiber(_) => {
                return true;
            }
            _ => {}
        }
    }

    false
}

impl<'a> DebugEventHandler for CrashDetectorEventHandler<'a> {
    fn on_exception(
        &mut self,
        debugger: &mut Debugger,
        info: &EXCEPTION_DEBUG_INFO,
        process_handle: HANDLE,
    ) -> DWORD {
        if !is_vcpp_notification(info, process_handle) {
            // An exception might be handled, or other cleanup might occur between
            // the first chance and the second chance, so we continue execution.
            let exception_code = info.ExceptionRecord.ExceptionCode;

            // If we're ignoring first chance exceptions, we skip collecting the stack
            // and adding the exception to our list of results.
            // We also ignore exceptions after we terminate any process as that might
            // cause exceptions in other processes in the process tree.
            if !(info.dwFirstChance == 1 && self.ignore_first_chance_exceptions
                || self.any_target_terminated)
            {
                match debugger.get_current_stack() {
                    Ok(stack) => self
                        .exceptions
                        .push(new_exception(process_handle, info, stack)),
                    Err(err) => error!("Error walking program under test stack: {}", err),
                }

                if info.dwFirstChance == 0 {
                    if exception_code == asan::EH_SANITIZER {
                        let asan_report =
                            asan::get_asan_report(process_handle, &info.ExceptionRecord);
                        if let Some(report) = asan_report {
                            self.debugger_output.push_str(&report);
                        }
                    }

                    // This is the second chance - we terminate the process to avoid
                    // any potential popups, e.g. from Windows Error Reporting.

                    // Kill the process but stay in the debug loop to consume
                    // the last EXIT_PROCESS_DEBUG_EVENT.
                    self.any_target_terminated = true;
                    trace!(
                        "crash in process {} - terminating",
                        process::id(process_handle)
                    );
                    process::terminate(process_handle);
                }
            }
        }

        // Continue normal exception handling processing
        DBG_EXCEPTION_NOT_HANDLED
    }

    fn on_output_debug_string(&mut self, _debugger: &mut Debugger, message: String) {
        self.debugger_output.push_str(&message);
    }

    fn on_output_debug_os_string(&mut self, _debugger: &mut Debugger, message: OsString) {
        self.debugger_output
            .push_str(message.to_string_lossy().as_ref());
    }

    fn on_poll(&mut self, debugger: &mut Debugger) {
        if let Err(e) = self.stdout.read(&mut self.stdout_buffer) {
            error!("Error reading child process stdout: {}", e);
        }
        if let Err(e) = self.stderr.read(&mut self.stderr_buffer) {
            error!("Error reading child process stderr: {}", e);
        }

        if !self.timed_out && self.start_time.elapsed() > self.max_duration {
            // The process appears to be hung, kill it and it's children.
            debug!("test timeout - terminating process tree");

            self.timed_out = true;
            self.any_target_terminated = true;

            debugger.quit_debugging();
        }
    }

    fn on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Some(coverage) = &mut self.coverage {
            if let Err(err) = coverage.on_create_process(dbg, module) {
                error!("error recording coverage on create process: {:?}", err);
                dbg.quit_debugging();
            }
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Some(coverage) = &mut self.coverage {
            if let Err(err) = coverage.on_load_dll(dbg, module) {
                error!("error recording coverage on load DLL: {:?}", err);
                dbg.quit_debugging();
            }
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if let Some(coverage) = &mut self.coverage {
            if let Err(err) = coverage.on_breakpoint(dbg, bp) {
                error!("error recording coverage on breakpoint: {:?}", err);
                dbg.quit_debugging();
            }
        }
    }
}

/// This function runs the application under a debugger to detect any crashes in
/// the process or any children processes.
pub fn test_process<'a>(
    app_path: impl AsRef<OsStr>,
    args: &[impl AsRef<OsStr>],
    env: &HashMap<String, String>,
    max_duration: Duration,
    ignore_first_chance_exceptions: bool,
    cache: Option<&'a mut ModuleCache>,
) -> Result<DebuggerResult> {
    debug!("Running: {}", logging::command_invocation(&app_path, args));

    let (stdout_reader, stdout_writer) = pipe()?;
    // To merge streams, we could instead use:
    //     stderr_writer = stdout_writer.try_clone()?;
    let (stderr_reader, stderr_writer) = pipe()?;
    let mut command = Command::new(app_path);
    command
        .args(args)
        .stdin(Stdio::null())
        .stdout(stdout_writer)
        .stderr(stderr_writer);

    for (k, v) in env {
        command.env(k, v);
    }

    let filter = coverage::code::CmdFilter::default();
    let recorder = cache.map(|c| BlockCoverageRecorder::new(c, filter));
    let start_time = Instant::now();
    let mut event_handler = CrashDetectorEventHandler::new(
        stdout_reader,
        stderr_reader,
        ignore_first_chance_exceptions,
        start_time,
        max_duration,
        recorder,
    );
    let (mut debugger, mut child) = Debugger::init(command, &mut event_handler)?;
    debugger.run(&mut event_handler)?;

    let pid = child.id();
    let status = child.wait()?;
    let output = Output {
        status,
        stdout: event_handler.stdout_buffer,
        stderr: event_handler.stderr_buffer,
    };
    debug!("TestTask: {:?}", logging::ProcessDetails::new(pid, &output));

    let exit_status = if event_handler.timed_out {
        ExitStatus::from_timeout(max_duration.as_secs())
    } else if let Some(code) = output.status.code() {
        ExitStatus::from_code(code)
    } else {
        unreachable!("Only Unix can signal");
    };

    Ok(DebuggerResult::new(
        filter_uninteresting_exceptions(event_handler.exceptions),
        exit_status,
        String::from_utf8_lossy(&output.stdout).to_string(),
        String::from_utf8_lossy(&output.stderr).to_string(),
        event_handler.debugger_output,
        event_handler.coverage.map(|r| r.into_coverage()),
    ))
}

fn filter_uninteresting_exceptions(mut exceptions: Vec<Exception>) -> Vec<Exception> {
    // Filter out first chance exceptions that are **immediately** followed by the same
    // second chance exception (same stack hash). This is the typical scenario.
    //
    // It is possible to have intervening handled exceptions between a first chance and
    // second chance (crashing) exception, but we keep those because it might be interesting.
    let mut i = 1;
    while i < exceptions.len() {
        let prev = &exceptions[i - 1];
        let curr = &exceptions[i];
        if prev.first_chance
            && prev.exception_code == curr.exception_code
            && prev.stack_hash == curr.stack_hash
        {
            exceptions.remove(i - 1);
        } else {
            i += 1;
        }
    }
    exceptions
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_result::{ExceptionCode, ExceptionDescription};

    const READ_AV: u32 = 0xc0000005;
    const EXCEPTION_CPP: u32 = 0xE06D7363;

    macro_rules! runps {
        ($timeout: expr, $script: expr) => {{
            test_process(
                r"C:\windows\system32\WindowsPowerShell\v1.0\powershell.exe",
                &["/nop".to_string(), "/c".to_string(), $script.to_string()],
                &HashMap::default(),
                $timeout,
                /*ignore first chance exceptions*/ true,
                None,
            )
            .unwrap()
        }};

        ($script: expr) => {{
            let timeout = Duration::from_secs(5);
            runps!(timeout, $script)
        }};
    }

    #[test]
    fn timeout_works() {
        let timeout = Duration::from_secs(2);
        let result = runps!(timeout, "sleep 600");
        assert_eq!(
            result.exit_status,
            ExitStatus::from_timeout(timeout.as_secs())
        );
    }

    #[test]
    #[allow(clippy::identity-op)]
    fn nonblocking_stdout() {
        let result = runps!(
            Duration::from_secs(10),
            "'+++' + 'a'*8kb + '@@@' + 'b'*8kb + '---'"
        );
        assert_eq!(result.exit_status, ExitStatus::from_code(0));

        assert!(result.stdout.starts_with("+++"));
        assert!(result.stdout.contains("@@@"));
        assert!(result.stdout.ends_with("---\r\n"));
        let expected_len = /*+++*/ 3 + (/*a - 8kb*/1 * 8 * 1024) + /*@@@*/3 + (/*b - 8kb*/1 * 8 * 1024) + /*---*/3 + /*\r\n*/2;
        assert_eq!(result.stdout.len(), expected_len);
    }

    macro_rules! exception {
        ($code: expr, $hash: expr, first) => {
            exception!($code, $hash, true)
        };

        ($code: expr, $hash: expr) => {
            exception!($code, $hash, false)
        };

        ($code: expr, $hash: expr, $first_chance: expr) => {{
            let descr = match $code {
                READ_AV => ExceptionCode::ReadAccessViolation,
                EXCEPTION_CPP => ExceptionCode::CppException,
                _ => ExceptionCode::UnknownExceptionCode,
            };
            Exception {
                exception_code: $code,
                description: ExceptionDescription::GenericException(descr),
                stack_hash: $hash,
                first_chance: $first_chance,
                stack_frames: vec![],
            }
        }};
    }

    #[test]
    fn test_exception_filtering() {
        let empty = vec![];
        let one_first_chance = vec![exception!(READ_AV, 1234, first)];
        let one_second_chance = vec![exception!(READ_AV, 1234)];
        let typical = vec![exception!(READ_AV, 1234, first), exception!(READ_AV, 1234)];
        let atypical1 = vec![exception!(READ_AV, 1234, first), exception!(READ_AV, 4567)];
        let atypical2 = vec![
            exception!(READ_AV, 1234, first),
            exception!(EXCEPTION_CPP, 1234),
        ];
        let atypical3 = vec![
            exception!(READ_AV, 1234, first),
            exception!(EXCEPTION_CPP, 1234, first),
            exception!(READ_AV, 1234),
        ];
        let atypical4 = vec![
            exception!(READ_AV, 1234, first),
            exception!(READ_AV, 4567, first),
            exception!(READ_AV, 1234),
        ];

        assert_eq!(filter_uninteresting_exceptions(empty).len(), 0);
        assert_eq!(filter_uninteresting_exceptions(one_first_chance).len(), 1);
        assert_eq!(filter_uninteresting_exceptions(one_second_chance).len(), 1);
        assert_eq!(filter_uninteresting_exceptions(typical).len(), 1);
        assert_eq!(filter_uninteresting_exceptions(atypical1).len(), 2);
        assert_eq!(filter_uninteresting_exceptions(atypical2).len(), 2);
        assert_eq!(filter_uninteresting_exceptions(atypical3).len(), 3);
        assert_eq!(filter_uninteresting_exceptions(atypical4).len(), 3);
    }
}
