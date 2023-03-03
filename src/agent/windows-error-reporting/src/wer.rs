use std::{
    cell::RefCell,
    ffi::c_void,
    fs::{self, File},
    io::Read,
    os::windows::process::CommandExt,
    path::Path,
    process::Command,
};

use anyhow::{bail, Context, Result};
use stacktrace_parser::CrashLog;
use tempfile::{tempfile, NamedTempFile};

use log::warn;
use win_util::pipe_handle::pipe;
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::{DBG_CONTINUE, DBG_EXCEPTION_NOT_HANDLED, EXCEPTION_BREAKPOINT, HANDLE, HWND},
        System::{
            ErrorReporting::{
                WerConsentApproved, WerDumpTypeMiniDump, WerFileTypeOther, WerReportAddDump,
                WerReportAddFile, WerReportCloseHandle, WerReportCreate, WerReportCritical,
                WerReportQueued, WerReportSetParameter, WerReportSubmit, WerReportUploaded,
                HREPORT, WER_DUMP_NOHEAP_ONQUEUE, WER_FILE_ANONYMOUS_DATA, WER_REPORT_INFORMATION,
                WER_SUBMIT_BYPASS_DATA_THROTTLING, WER_SUBMIT_NO_QUEUE,
                WER_SUBMIT_REPORT_MACHINE_ID, WER_SUBMIT_RESULT,
            },
            Threading::{GetCurrentProcess, DEBUG_ONLY_THIS_PROCESS},
        },
    },
};

const EVENT_NAME: &str = "onefuzz_crash";

use debugger::{DebugEventHandler, Debugger};

/// Event handler for Windows Error Reporting (WER) crash reporting.
/// A report will be created and submitted when a breakpoint is hit.
/// We expect the breakpoint to be hit when the __debugbreak() is called.
struct WerDebugEventHandler<'a> {
    /// Path to the target executable.
    target_exe: &'a Path,
    /// the WER report handle.
    pub report: RefCell<Option<WerReport>>,
    /// name of the event to report.
    event_name: &'a str,
}

impl<'a> WerDebugEventHandler<'a> {
    fn new(target_exe: &'a Path, event_name: &'a str) -> Result<Self> {
        let report = RefCell::new(None);

        Ok(WerDebugEventHandler {
            target_exe,
            report,
            event_name,
        })
    }

    fn create_report(
        &self,
        _debugger: &mut Debugger,
        _info: &debugger::ExceptionDebugInfo,
        process_handle: *mut c_void,
        event_name: &str,
    ) -> Result<u32> {
        let process_handle = HANDLE(process_handle as isize);
        let app_name = self
            .target_exe
            .file_name()
            .and_then(|f| f.to_str())
            .ok_or(anyhow::anyhow!("invalid target_exe path"))?;
        println!("**** app_name: {}", app_name);
        let report = WerReport::create(
            process_handle,
            event_name,
            app_name,
            self.target_exe,
            "libfuzzer crash detected by onefuzz",
        )
        .context("failed to create WER report")?;

        if self.report.borrow().is_none() {
            report.add_dump(process_handle)?;
            self.report.replace(Some(report));
        }
        Ok(DBG_CONTINUE.0 as u32)
    }
}

impl<'a> DebugEventHandler for WerDebugEventHandler<'a> {
    fn on_exception(
        &mut self,
        debugger: &mut Debugger,
        info: &debugger::ExceptionDebugInfo,
        process_handle: *mut c_void,
    ) -> u32 {
        if info.ExceptionRecord.ExceptionCode == EXCEPTION_BREAKPOINT.0 as u32 {
            match self.create_report(debugger, info, process_handle, self.event_name) {
                Ok(result) => return result,
                Err(err) => {
                    warn!("failed to report to WER: {}", err);
                }
            }
        }
        DBG_EXCEPTION_NOT_HANDLED.0 as u32
    }
}

fn to_u16<const N: usize>(str: &str) -> [u16; N] {
    let mut arr: [u16; N] = [0; N];

    for (count, c) in str.encode_utf16().enumerate() {
        if count >= N {
            break;
        }

        arr[count] = c;
    }
    arr
}

pub struct WerReport {
    inner_report: HREPORT,
}

impl Drop for WerReport {
    fn drop(&mut self) {
        unsafe {
            WerReportCloseHandle(self.inner_report).unwrap();
        }
    }
}

impl From<HREPORT> for WerReport {
    fn from(report: HREPORT) -> Self {
        WerReport {
            inner_report: report,
        }
    }
}

impl WerReport {
    pub fn submit(&self) -> Result<WER_SUBMIT_RESULT> {
        unsafe {
            let result = WerReportSubmit(
                self.inner_report,
                WerConsentApproved,
                //WER_SUBMIT_NO_QUEUE
                WER_SUBMIT_REPORT_MACHINE_ID | WER_SUBMIT_BYPASS_DATA_THROTTLING,
            )?;
            Ok(result)
        }
    }

    pub fn create(
        process_handle: HANDLE,
        event_name: &str,
        application_name: &str,
        application_path: impl AsRef<Path>,
        description: &str,
    ) -> Result<Self> {
        let event_name = to_u16::<64>(event_name);

        let report_info = WER_REPORT_INFORMATION {
            dwSize: std::mem::size_of::<WER_REPORT_INFORMATION>() as u32,
            hProcess: process_handle,
            wzConsentKey: to_u16::<64>(""),
            wzFriendlyEventName: to_u16::<128>(application_name),
            wzApplicationName: to_u16::<128>(application_name),
            wzApplicationPath: to_u16::<260>(application_path.as_ref().to_string_lossy().as_ref()),
            wzDescription: to_u16::<512>(description),
            hwndParent: HWND(0),
        };

        let report = unsafe {
            WerReportCreate(
                PCWSTR::from_raw(event_name.as_ptr()),
                WerReportCritical,
                Some(&report_info as *const _),
            )?
        };
        let wer_report = WerReport::from(report);
        Ok(wer_report)
    }

    pub fn set_parameters(&self, parameters: &[(&str, &str)]) -> Result<()> {
        if parameters.len() > 10 {
            return Err(anyhow::anyhow!("too many parameters"));
        }

        for (index, &(name, value)) in parameters.iter().enumerate() {
            unsafe {
                WerReportSetParameter(
                    self.inner_report,
                    index as u32,
                    PCWSTR::from_raw(to_u16::<64>(name).as_ptr()),
                    PCWSTR::from_raw(to_u16::<64>(value).as_ptr()),
                )?;
            }
        }

        Ok(())
    }

    pub fn add_file(&self, path: impl AsRef<Path>) -> Result<()> {
        unsafe {
            WerReportAddFile(
                self.inner_report,
                PCWSTR::from_raw(to_u16::<260>(path.as_ref().to_string_lossy().as_ref()).as_ptr()),
                WerFileTypeOther,
                WER_FILE_ANONYMOUS_DATA,
            )?;
        }
        Ok(())
    }

    pub fn add_dump(&self, process_handle: HANDLE) -> Result<()> {
        println!("**** adding dump");

        unsafe {
            WerReportAddDump(
                self.inner_report,
                process_handle,
                None,
                WerDumpTypeMiniDump,
                None,
                None,
                WER_DUMP_NOHEAP_ONQUEUE,
            )?;
        }

        println!("**** dump added");
        Ok(())
    }

    pub fn create_wer_report(
        target_exe: &Path,
        input_file: Option<&Path>,
        output_file: &Path,
    ) -> Result<WerReport> {
        let mut target = Command::new(target_exe);
        if let Some(input_file) = input_file {
            target.args(&[format!("{}", input_file.to_string_lossy())]);
        }
        let file = fs::File::open(output_file)?;
        target
            .creation_flags(DEBUG_ONLY_THIS_PROCESS.0)
            .env("ASAN_OPTIONS", "abort_on_error=true")
            .stdout(file);

        let mut handler = WerDebugEventHandler::new(target_exe, EVENT_NAME)?;

        let (mut debugger, _child) = Debugger::init(target, &mut handler)?;
        debugger
            .run(&mut handler)
            .context("failed to run debugger")?;

        if let Some(report) = handler.report.take() {
            return Ok(report);
        }

        bail!("failed to create report");
    }

    pub fn report_crash(target_exe: &Path, input_file: Option<&Path>) -> Result<()> {
        let temp_file = NamedTempFile::new()?;
        let mut file = temp_file.reopen()?;

        let mut target = Command::new(target_exe);
        if let Some(input_file) = input_file {
            target.args(&[format!("{}", input_file.to_string_lossy())]);
        }
        target
            // .arg("-exact_artifact_path")
            // .arg(format!(""))
            .creation_flags(DEBUG_ONLY_THIS_PROCESS.0)
            .env("ASAN_OPTIONS", "abort_on_error=true")
            .stdout(file);
        //.stderr(stderr_writer);

        let mut handler = WerDebugEventHandler::new(target_exe, "crash64")?;

        let (mut debugger, _child) = Debugger::init(target, &mut handler)?;
        debugger
            .run(&mut handler)
            .context("failed to run debugger")?;

        println!("report_crash 3");

        if let Some(report) = handler.report.take() {
            // if let Ok(crash_report) = crash_report{
            //     report.add_file(temp_file.path());

            println!("report_crash 4");
            // add other metadata here

            report.set_parameters(&[
                (
                    "ApplicationName",
                    target_exe.file_name().unwrap().to_str().unwrap(),
                ),
                ("ApplicationVersion", "0.0.0.0"),
                ("ApplicationStamp", "7a8f0e64"),
                ("ModuleName", "clang_rt.asan_dynamic-x86_64.dll"),
                ("ModeuleVersion", "0.0.0.0"),
                ("ModuleStamp", "6354d30b"),
                ("ExceptionCode", "80000003"),
                ("Offset", "000000000001996e"),
            ])?;
            println!("report_crash 5");

            report.add_file(temp_file.path())?;

            let result = report.submit()?;

            if result != WerReportUploaded && result != WerReportQueued {
                return Err(anyhow::anyhow!("failed to submit WER report: {}", result.0));
            }
            println!("report_crash 6");
        }

        // let pid = child.id();
        // let status = child.wait()?;
        // let stdout = stdout_reader.;
        // let stderr = stderr_buffer;

        Ok(())
    }

    pub fn report_current_process() -> Result<()> {
        unsafe {
            let report = WerReport::create(
                GetCurrentProcess(),
                "SampleGenericReport",
                "WerReportCurrentProcess",
                std::env::current_exe()?,
                "WerReportCurrentProcess",
            )?;

            report.add_dump(GetCurrentProcess())?;

            report.submit()?;
        }

        Ok(())
    }
}
