use clap::Parser;
use std::ffi::OsStr;
use windows_error_reporting::wer::WerReport;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum SubmitOptions {
    current,
    target{
        #[clap(short, long)]
        target_exe: String,

        #[clap(short, long)]
        input: String,
    },
}

// #[derive(Parser, Debug)]
// pub struct Current {}

// #[derive(Parser, Debug)]
// pub struct Target

fn main() {
    match SubmitOptions::parse(){
        SubmitOptions::current => {
            WerReport::report_current_process().unwrap();
        }
        SubmitOptions::target { target_exe, input } => {
            WerReport::report_crash(
                //OsStr::new("C:\\temp\\onefuzz_sample\\onefuzz-sample\\onefuzz_sample.exe"),
                OsStr::new(target_exe.as_str()),
                //"C:\\temp\\onefuzz_sample\\onefuzz-sample\\crash-265682293fb3a15c75213499359083bf2551717a"
                input.as_str(),
            )
            .unwrap();
        }
    }
}
