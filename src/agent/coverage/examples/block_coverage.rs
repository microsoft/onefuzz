// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::process::Command;

use anyhow::Result;
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[cfg(target_os = "linux")]
    #[structopt(short, long)]
    filter: Option<std::path::PathBuf>,

    #[structopt(min_values = 1)]
    cmd: Vec<String>,
}

#[cfg(target_os = "windows")]
fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();
    log::info!("recording coverage for: {:?}", opt.cmd);

    let mut cmd = Command::new(&opt.cmd[0]);
    cmd.args(&opt.cmd[1..]);

    let coverage = coverage::block::windows::record(cmd)?;

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
    use coverage::code::{CmdFilter, CmdFilterDef};

    env_logger::init();

    let opt = Opt::from_args();
    let filter = if let Some(path) = &opt.filter {
        let data = std::fs::read(path)?;
        let def: CmdFilterDef = serde_json::from_slice(&data)?;
        CmdFilter::new(def)?
    } else {
        CmdFilter::default()
    };

    let mut cmd = Command::new(&opt.cmd[0]);
    cmd.args(&opt.cmd[1..]);

    let mut recorder = Recorder::new(filter);
    recorder.record(cmd)?;

    for (module_path, cov) in recorder.coverage.iter() {
        let mut hit = 0;
        let mut found = 0;

        let name = module_path.name_lossy();

        log::info!("{}", module_path);

        for block in cov.blocks.values() {
            found += 1;

            if block.count > 0 {
                hit += 1;
            }

            let marker = if block.count == 0 { " " } else { "x" };

            let module = recorder
                .modules
                .cached
                .get(module_path)
                .expect("unreachable: module with coverage not in recorder cache");

            if let Some(sym) = module.module.symbols.find(block.offset) {
                let sym_offset = block.offset - sym.image_offset;
                log::debug!(
                    "  [{}] {}+{:x} ({}+{:x})",
                    marker,
                    name,
                    block.offset,
                    sym.name,
                    sym_offset,
                );
            } else {
                log::debug!("  [{}] {}+{:x}", marker, name, block.offset);
            }
        }

        let percent = 100.0 * (hit as f64) / (found as f64);
        log::info!("block coverage = {}/{} ({:.2}%)", hit, found, percent);
    }

    Ok(())
}
