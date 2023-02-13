use std::{ffi::OsStr, os::windows::process::CommandExt, path::Path, process::Command};

use anyhow::{Context, Result};
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::{HANDLE, HWND},
        System::{
            ErrorReporting::{
                WerConsentApproved, WerDumpTypeMiniDump, WerReportAddDump,
                WerReportApplicationCrash, WerReportCloseHandle, WerReportCreate, WerReportSubmit,
                HREPORT, WER_DUMP_NOHEAP_ONQUEUE, WER_REPORT_INFORMATION, WER_SUBMIT_FLAGS,
            },
            Threading::DEBUG_ONLY_THIS_PROCESS,
        },
    },
};

use debugger::{DebugEventHandler, Debugger};

struct WerDebugEventHandler<'a> {
    path: &'a OsStr,
}

impl<'a> WerDebugEventHandler<'a> {
    fn new(path: &'a OsStr) -> Result<Self> {
        Ok(WerDebugEventHandler { path })
    }

    fn _on_exit_process(&mut self, debugger: &mut Debugger, _exit_code: u32) -> Result<()> {
        println!("on_exit_process 1");
        let process_handle = debugger.target().process_handle();
        println!("on_exit_process 2");

        let app_name = self
            .path
            .to_str()
            .ok_or(anyhow::anyhow!("invalid target_exe path"))?;
        let report = WerReport::create(
            HANDLE(process_handle as isize),
            "libfuzzer_crash",
            app_name,
            self.path,
            "libfuzzer crash detected by onefuzz",
        )
        .context("failed to create WER report")?;
        println!("on_exit_process 3");
        report.submit().context("failed to submit WER report")?;
        println!("on_exit_process 4");
        Ok(())
    }
}

impl<'a> DebugEventHandler for WerDebugEventHandler<'a> {
    fn on_exit_process(&mut self, debugger: &mut Debugger, exit_code: u32) {
        match WerDebugEventHandler::_on_exit_process(self, debugger, exit_code) {
            Ok(_) => (),
            Err(e) => {
                // todo
                eprintln!("Error: {}", e);
            }
        }
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

struct WerReport {
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
    pub fn submit(
        &self,
    ) -> Result<windows::Win32::System::ErrorReporting::WER_SUBMIT_RESULT, windows::core::Error>
    {
        unsafe { WerReportSubmit(self.inner_report, WerConsentApproved, WER_SUBMIT_FLAGS(0)) }
    }

    pub fn create(
        process_handle: HANDLE,
        event_name: &str,
        application_name: &str,
        path: impl AsRef<Path>,
        description: &str,
    ) -> Result<Self> {
        let event_name = to_u16::<64>(event_name);

        let report_info = WER_REPORT_INFORMATION {
            dwSize: std::mem::size_of::<WER_REPORT_INFORMATION>() as u32,
            hProcess: process_handle,
            wzConsentKey: event_name,
            wzFriendlyEventName: to_u16::<128>(""),
            wzApplicationName: to_u16::<128>(application_name),
            wzApplicationPath: to_u16::<260>(path.as_ref().to_string_lossy().as_ref()),
            wzDescription: to_u16::<512>(description),
            hwndParent: HWND(0),
        };

        unsafe {
            let report = WerReportCreate(
                PCWSTR::from_raw(event_name.as_ptr()),
                WerReportApplicationCrash,
                Some(&report_info as *const _),
            )?;

            WerReportAddDump(
                report,
                process_handle,
                None,
                WerDumpTypeMiniDump,
                None,
                None,
                WER_DUMP_NOHEAP_ONQUEUE,
            )?;

            Ok(report.into())
        }
    }

    pub fn report_crash(target_exe: &OsStr, target_options: Vec<String>) -> Result<()> {
        let mut target = Command::new(
            target_exe
                .to_str()
                .ok_or(anyhow::anyhow!("invalid target_exe path"))?,
        );
        target
            .args(&target_options)
            .creation_flags(DEBUG_ONLY_THIS_PROCESS.0)
            .env("ASAN_OPTIONS", "abort_on_error=1");

        let mut handler = WerDebugEventHandler::new(target_exe)?;

        let (mut debugger, _child) = Debugger::init(target, &mut handler)?;
        debugger
            .run(&mut handler)
            .context("failed to run debugger")?;

        Ok(())
    }
}

#[cfg(test)]
mod tests {

    use super::*;
    use debugger::Debugger;

    #[test]
    fn test1() {
        WerReport::report_crash(
            OsStr::new("C:\\work\\scratch\\watson\\integration-tests\\libfuzzer\\fuzz.exe"),
            vec!["C:\\work\\scratch\\watson\\integration-tests\\libfuzzer\\crash-24dd304aabea149efcdbbdf59be46bad3f4d289e".into()])
            .unwrap();
    }
}
