use clap::Parser;
use std::ffi::OsStr;
use windows_error_reporting::wer::WerReport;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub struct SubmitOptions {
    #[clap(short, long)]
    target_exe: String,

    #[clap(short, long)]
    input: String,
}

fn main() {
    let opt = SubmitOptions::parse();

    WerReport::report_crash(
        //OsStr::new("C:\\temp\\onefuzz_sample\\onefuzz-sample\\onefuzz_sample.exe"),
        OsStr::new(opt.target_exe.as_str()),
        //"C:\\temp\\onefuzz_sample\\onefuzz-sample\\crash-265682293fb3a15c75213499359083bf2551717a"
        opt.input.as_str(),
    )
    .unwrap();
}
