// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use clap::Parser;
use onefuzz::{input_tester::Tester, machine_id::MachineIdentity};

#[derive(Debug, PartialEq, Eq, Parser)]
#[command(name = "test-input")]
struct Opt {
    #[arg(short, long)]
    pub exe: PathBuf,

    #[arg(short, long, long_help = "Defaults to `{input}`")]
    pub options: Vec<String>,

    #[arg(short, long, long_help = "Defaults to dir of `exe`")]
    pub setup_dir: Option<PathBuf>,

    #[arg(short, long)]
    pub input: PathBuf,

    #[arg(long)]
    pub check_asan_log: bool,

    #[arg(long)]
    pub check_asan_stderr: bool,

    #[arg(long)]
    pub no_check_debugger: bool,

    #[arg(short, long, long_help = "Timeout (seconds)", default_value = "5")]
    pub timeout: u64,
}

#[tokio::main]
async fn main() -> Result<()> {
    let opt = Opt::parse();

    // Default `setup_dir` to base dir of
    let setup_dir = opt.setup_dir.clone().unwrap_or_else(|| {
        opt.exe
            .parent()
            .expect("target exe missing file component")
            .to_owned()
    });

    let mut target_options = opt.options.clone();
    if target_options.is_empty() {
        target_options.push("{input}".into());
    }

    let env = Default::default();
    let tester = Tester::new(
        &setup_dir,
        None,
        &opt.exe,
        &target_options,
        &env,
        MachineIdentity {
            machine_id: uuid::Uuid::new_v4(),
            machine_name: "test-input".into(),
            scaleset_name: None,
        },
    );

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
        let text: &str = crash.text.as_deref().unwrap_or_default();
        println!("    sanitizer = {}", crash.sanitizer);
        println!("    summary = {}", crash.summary);
        println!("    text = {text}");
    } else {
        println!("[-] no crash detected.");
    }

    println!();
    println!("[+] verbose test result:");
    println!();
    println!("{test_result:#?}");

    Ok(())
}
