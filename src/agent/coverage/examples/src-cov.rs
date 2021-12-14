// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};
use std::{process::Command, process::Stdio};

use anyhow::Result;
use coverage::block::CommandBlockCov as Coverage;
use coverage::cache::ModuleCache;
use coverage::code::CmdFilter;
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[structopt(short, long, min_values = 1)]
    inputs: Vec<PathBuf>,

    #[structopt(short, long)]
    dir: Option<PathBuf>,

    #[structopt(min_values = 2)]
    cmd: Vec<String>,

    #[structopt(short, long, long_help = "Timeout in ms", default_value = "5000")]
    timeout: u64,
}

fn main() -> Result<()> {
    let opt = Opt::from_args();
    let filter = CmdFilter::default();

    let mut cache = ModuleCache::default();
    let mut total = Coverage::default();
    let timeout = Duration::from_millis(opt.timeout);

    if let Some(dir) = &opt.dir {
        for entry in std::fs::read_dir(dir)? {
            let input = entry?.path();

            println!("testing input: {}", input.display());

            let cmd = input_command(&opt.cmd, &input);
            let coverage = record(&mut cache, filter.clone(), cmd, timeout)?;

            total.merge_max(&coverage);
        }
    }

    for input in &opt.inputs {
        println!("testing input: {}", input.display());

        let cmd = input_command(&opt.cmd, input);
        let coverage = record(&mut cache, filter.clone(), cmd, timeout)?;

        total.merge_max(&coverage);
    }

    let mut debug_info = coverage::debuginfo::DebugInfo::default();
    let src_coverage = total.source_coverage(&mut debug_info)?;

    for file_coverage in src_coverage.files {
        for location in &file_coverage.locations {
            println!(
                "{} {}:{}",
                location.count, file_coverage.file, location.line
            );
        }
    }

    Ok(())
}

fn input_command(argv: &[String], input: &Path) -> Command {
    let mut cmd = Command::new(&argv[0]);
    cmd.stdin(Stdio::null());
    cmd.stderr(Stdio::null());
    cmd.stdout(Stdio::null());

    let args: Vec<_> = argv[1..]
        .iter()
        .map(|a| {
            if &*a == "@@" {
                input.display().to_string()
            } else {
                a.to_string()
            }
        })
        .collect();

    cmd.args(&args);

    cmd
}

#[cfg(target_os = "macos")]
fn record(
    _cache: &mut ModuleCache,
    _filter: CmdFilter,
    _cmd: Command,
    _timeout: Duration,
) -> Result<Coverage> {
    unimplemented!("coverage recording is not supported on macOS");
}

#[cfg(target_os = "linux")]
fn record(
    cache: &mut ModuleCache,
    filter: CmdFilter,
    cmd: Command,
    timeout: Duration,
) -> Result<Coverage> {
    use coverage::block::linux::Recorder;

    let now = Instant::now();

    let coverage = Recorder::record(cmd, timeout, cache, filter.clone())?;

    let elapsed = now.elapsed();
    log::info!("recorded in {:?}", elapsed);

    Ok(coverage)
}

#[cfg(target_os = "windows")]
fn record(
    cache: &mut ModuleCache,
    filter: CmdFilter,
    cmd: Command,
    timeout: Duration,
) -> Result<Coverage> {
    use coverage::block::windows::{Recorder, RecorderEventHandler};

    let mut recorder = Recorder::new(cache, filter);
    let mut handler = RecorderEventHandler::new(&mut recorder, timeout);

    let now = Instant::now();

    handler.run(cmd)?;

    let elapsed = now.elapsed();
    log::info!("recorded in {:?}", elapsed);

    Ok(recorder.into_coverage())
}
