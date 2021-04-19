// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{process::Command, process::Stdio};

use anyhow::Result;
use coverage::code::{CmdFilter, CmdFilterDef};
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[structopt(short, long)]
    filter: Option<std::path::PathBuf>,

    #[structopt(min_values = 1)]
    cmd: Vec<String>,

    #[structopt(short, long, default_value = "5")]
    timeout: u64,
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

#[cfg(target_os = "windows")]
fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();
    let filter = opt.load_filter_or_default()?;

    log::info!("recording coverage for: {:?}", opt.cmd);

    let mut cmd = Command::new(&opt.cmd[0]);
    cmd.args(&opt.cmd[1..]);

    let timeout = std::time::Duration::from_secs(opt.timeout);
    let coverage = coverage::block::windows::record(cmd, filter, timeout)?;

    for (module, cov) in coverage.iter() {
        let total = cov.blocks.len();
        let hit: u32 = cov.blocks.values().map(|b| b.count).sum();
        let percent = 100.0 * (hit as f64) / (total as f64);
        log::info!("module = {}, {} / {} ({:.2}%)", module, hit, total, percent);
    }

    Ok(())
}

#[cfg(target_os = "linux")]
fn main() -> Result<()> {
    use coverage::block::linux::Recorder;
    use coverage::cache::ModuleCache;

    env_logger::init();

    let opt = Opt::from_args();
    let filter = opt.load_filter_or_default()?;

    let mut cmd = Command::new(&opt.cmd[0]);
    cmd.stdin(std::process::Stdio::null()).args(&opt.cmd[1..]);

    let mut cache = ModuleCache::default();
    let mut recorder = Recorder::new(&mut cache, filter);
    recorder.record(cmd)?;

    let coverage = serde_json::to_string_pretty(recorder.coverage())?;
    println!("{}", coverage);

    Ok(())
}
