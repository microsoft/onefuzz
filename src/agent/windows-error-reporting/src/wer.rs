use std::{ffi::c_void, os::windows::process::CommandExt, path::{Path, PathBuf}, process::Command, cell::RefCell};

use anyhow::{Context, Result};

use log::warn;
use uuid::Uuid;
use win_util::pipe_handle::pipe;
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::{DBG_CONTINUE, DBG_EXCEPTION_NOT_HANDLED, EXCEPTION_BREAKPOINT, HANDLE, HWND},
        System::{
            ErrorReporting::{
                WerConsentApproved, WerDumpTypeMiniDump, WerFileTypeOther, WerReportAddDump,
                WerReportAddFile, WerReportCloseHandle, WerReportCreate, WerReportCritical,
                WerReportSetParameter, WerReportSubmit, WerReportUploaded, HREPORT,
                WER_DUMP_NOHEAP_ONQUEUE, WER_FILE_ANONYMOUS_DATA, WER_REPORT_INFORMATION,
                WER_SUBMIT_BYPASS_DATA_THROTTLING, WER_SUBMIT_NO_QUEUE,
                WER_SUBMIT_REPORT_MACHINE_ID, WER_SUBMIT_RESULT,
            },
            Threading::{DEBUG_ONLY_THIS_PROCESS, GetCurrentProcess},
        },
    },
};

const EVENT_NAME:&str = "onefuzz_crash";

use debugger::{DebugEventHandler, Debugger};

/// Event handler for Windows Error Reporting (WER) crash reporting.
/// A report will be created and submitted when a breakpoint is hit.
/// We expect the breakpoint to be hit when the __debugbreak() is called.
struct WerDebugEventHandler<'a> {
    /// Path to the target executable.
    target_exe: &'a Path,
    /// the WER report handle.
    // report: OnceCell<WerReport>,
    /// the path to the input file.
    input_file: Option<&'a Path>,

    pub report: RefCell<Option<WerReport>>,

}

impl<'a> WerDebugEventHandler<'a> {
    fn new(target_exe: &'a Path, input_file: Option<&'a Path>) -> Result<Self> {
        let report = RefCell::new(None);

        Ok(WerDebugEventHandler {
            target_exe,
            report,
            input_file,
        })
    }

    fn create_report(
        &self,
        _debugger: &mut Debugger,
        _info: &debugger::ExceptionDebugInfo,
        process_handle: *mut c_void,
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
            EVENT_NAME,
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
            match self.create_report(debugger, info, process_handle) {
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
                WER_SUBMIT_NO_QUEUE
                    | WER_SUBMIT_REPORT_MACHINE_ID
                    | WER_SUBMIT_BYPASS_DATA_THROTTLING,
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
            wzFriendlyEventName: to_u16::<128>(""),
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


        // wer_report.set_parameters(&vec![
        //     ("Param1", "Value1"),
        //     ("Param2", "Value2"),
        //     ("Param3", "Value3"),
        // ])?;

        // if let Some(input_path) = input_path {
        //     wer_report.add_file(input_path)?;
        // }

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
        Ok(())
    }

    pub fn report_crash(target_exe: &Path, input_file: Option<&Path>) -> Result<()> {
        let (stdout_reader, stdout_writer) = pipe()?;
        // let (stderr_reader, stderr_writer) = pipe()?;
        let input = input_file.as_ref().map(|path| path.to_string_lossy().to_string()).unwrap_or(Uuid::new_v4().to_string());

        let mut target = Command::new(target_exe);
        if let Some(input_file) = input_file {
            target
                .args(&[format!("{}", input_file.to_string_lossy() )]);
        }
        target
            .arg("-exact_artifact_path")
            .arg(format!(""))
            .creation_flags(DEBUG_ONLY_THIS_PROCESS.0)
            .env("ASAN_OPTIONS", "abort_on_error=true")

            ;
            //.stdout(stdout_writer)
            //.stderr(stderr_writer);

        let test = &input_file.as_deref();
        let mut handler = WerDebugEventHandler::new(target_exe.as_ref(), input_file)?;

        let (mut debugger, _child) = Debugger::init(target, &mut handler)?;
        debugger
            .run(&mut handler)
            .context("failed to run debugger")?;

        println!("report_crash 3");
        if let Some(report )= handler.report.take() {
            println!("report_crash 4");
            // add other metadata here
            report.set_parameters(&vec![
                ("Param1", "Value1"),
                ("Param2", "Value2"),
                ("Param3", "Value3"),
            ])?;
            println!("report_crash 5");
            let result = report.submit()?;

            if result != WerReportUploaded {
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
