// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};
use std::time::Duration;
use std::{process::Command, process::Stdio};

use anyhow::Result;
use coverage_legacy::block::CommandBlockCov as Coverage;
use coverage_legacy::cache::ModuleCache;
use coverage_legacy::code::{CmdFilter, CmdFilterDef};
use structopt::StructOpt;

#[derive(Debug, PartialEq, Eq, StructOpt)]
struct Opt {
    #[structopt(short, long)]
    filter: Option<PathBuf>,

    #[structopt(short, long, min_values = 1)]
    inputs: Vec<PathBuf>,

    #[structopt(min_values = 2)]
    cmd: Vec<String>,

    #[structopt(short, long, long_help = "Timeout in ms", default_value = "5000")]
    timeout: u64,

    #[structopt(long)]
    modoff: bool,
}

impl Opt {
    pub fn load_filter_or_default(&self) -> Result<CmdFilter> {
        if let Some(path) = &self.filter {
            let data = std::fs::read(path)?;
            let def: CmdFilterDef = serde_json::from_slice(&data)?;
            CmdFilter::new(def)
        } else {
            Ok(CmdFilter::default())
        }
    }
}

fn main() -> Result<()> {
    let opt = Opt::from_args();
    let filter = opt.load_filter_or_default()?;

    env_logger::init();

    let mut cache = ModuleCache::default();
    let mut total = Coverage::default();
    let timeout = Duration::from_millis(opt.timeout);

    for input in &opt.inputs {
        let cmd = input_command(&opt.cmd, input);
        let coverage = record(&mut cache, filter.clone(), cmd, timeout)?;

        log::info!("input = {}", input.display());
        if !opt.modoff {
            print_stats(&coverage);
        }

        total.merge_max(&coverage);
    }

    if opt.modoff {
        print_modoff(&total);
    } else {
        print_stats(&total);
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
            if a == "@@" {
                input.display().to_string()
            } else {
                a.to_string()
            }
        })
        .collect();

    cmd.args(&args);

    cmd
}

#[cfg(target_os = "linux")]
fn record(
    cache: &mut ModuleCache,
    filter: CmdFilter,
    cmd: Command,
    timeout: Duration,
) -> Result<Coverage> {
    use coverage_legacy::block::linux::Recorder;

    let now = std::time::Instant::now();

    let coverage = Recorder::record(cmd, timeout, cache, filter)?;

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
    use coverage_legacy::block::windows::{Recorder, RecorderEventHandler};

    let mut recorder = Recorder::new(cache, filter);
    let mut handler = RecorderEventHandler::new(&mut recorder, timeout);

    let now = std::time::Instant::now();

    handler.run(cmd)?;

    let elapsed = now.elapsed();
    log::info!("recorded in {:?}", elapsed);

    Ok(recorder.into_coverage())
}

fn print_stats(coverage: &Coverage) {
    for (m, c) in coverage.iter() {
        let covered = c.covered_blocks();
        let known = c.known_blocks();
        let percent = 100.0 * (covered as f64) / (known as f64);
        log::info!(
            "{} = {} / {} ({:.2}%)",
            m.name_lossy(),
            covered,
            known,
            percent
        );
    }
}

fn print_modoff(coverage: &Coverage) {
    for (m, c) in coverage.iter() {
        for b in c.blocks.values() {
            if b.count > 0 {
                println!("{}+{:x}", m.name_lossy(), b.offset);
            }
        }
    }
}
