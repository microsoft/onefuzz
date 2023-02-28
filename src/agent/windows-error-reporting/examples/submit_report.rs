use clap::Parser;
use std::path::PathBuf;
use windows_error_reporting::wer::WerReport;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum SubmitOptions {
    Current,
    Target {
        #[clap(short, long)]
        target_exe: PathBuf,

        #[clap(short, long)]
        input: PathBuf,
    },
}

// #[derive(Parser, Debug)]
// pub struct Current {}

// #[derive(Parser, Debug)]
// pub struct Target

fn main() {
    match SubmitOptions::parse() {
        SubmitOptions::Current => {
            WerReport::report_current_process().unwrap();
        }
        SubmitOptions::Target { target_exe, input } => {
            WerReport::report_crash(
                //OsStr::new("C:\\temp\\onefuzz_sample\\onefuzz-sample\\onefuzz_sample.exe"),
                &target_exe,
                //"C:\\temp\\onefuzz_sample\\onefuzz-sample\\crash-265682293fb3a15c75213499359083bf2551717a"
                Some(&input),
            )
            .unwrap();
        }
    }
}
