// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use onefuzz::input_tester::Tester;
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[structopt(short, long)]
    pub exe: PathBuf,

    #[structopt(short, long)]
    pub options: Vec<String>,

    #[structopt(short, long, long_help = "Defaults to dir of `exe`")]
    pub setup_dir: Option<PathBuf>,

    #[structopt(short, long)]
    pub input: PathBuf,

    #[structopt(long)]
    pub check_asan_log: bool,

    #[structopt(long)]
    pub check_asan_stderr: bool,

    #[structopt(long)]
    pub no_check_debugger: bool,

    #[structopt(short, long, long_help = "Timeout (seconds)", default_value = "5")]
    pub timeout: u64,
}

#[tokio::main]
async fn main() -> Result<()> {
    let opt = Opt::from_args();

    // Default `setup_dir` to base dir of
    let setup_dir = opt.setup_dir.clone().unwrap_or_else(|| {
        opt.exe
            .parent()
            .expect("target exe missing file component")
            .to_owned()
    });

    let env = Default::default();
    let tester = Tester::new(&setup_dir, &opt.exe, &opt.options, &env);

    let check_debugger = !opt.no_check_debugger;
    let tester = tester
        .timeout(opt.timeout)
        .check_debugger(check_debugger)
        .check_asan_log(opt.check_asan_log)
        .check_asan_stderr(opt.check_asan_stderr);

    let test_result = tester.test_input(&opt.input).await?;

    if let Some(crash) = test_result.crash_log.as_ref() {
        println!("[+] crash detected!");
        println!();
        let text: &str = crash.text.as_ref().map(|s| s.as_str()).unwrap_or_default();
        println!("    sanitizer = {}", crash.sanitizer);
        println!("    summary = {}", crash.summary);
        println!("    text = {}", text);
    } else {
        println!("[-] no crash detected.");
    }

    println!();
    println!("[+] verbose test result:");
    println!();
    println!("{:?}", test_result);

    Ok(())
}
