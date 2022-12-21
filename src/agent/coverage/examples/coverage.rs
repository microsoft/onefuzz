use std::process::Command;
use std::time::Duration;

use anyhow::Result;
use clap::Parser;
use coverage::allowlist::{AllowList, TargetAllowList};
use coverage::binary::BinaryCoverage;
use coverage::record::CoverageRecorder;
use debuggable_module::loader::Loader;

#[derive(Parser, Debug)]
struct Args {
    #[arg(long)]
    module_allowlist: Option<String>,

    #[arg(long)]
    source_allowlist: Option<String>,

    #[arg(short, long)]
    timeout: Option<u64>,

    command: Vec<String>,
}

const DEFAULT_TIMEOUT: Duration = Duration::from_secs(5);

fn main() -> Result<()> {
    let args = Args::parse();

    let timeout = args
        .timeout
        .map(Duration::from_millis)
        .unwrap_or(DEFAULT_TIMEOUT);

    let mut cmd = Command::new(&args.command[0]);
    if args.command.len() > 1 {
        cmd.args(&args.command[1..]);
    }

    let mut allowlist = TargetAllowList::default();

    if let Some(path) = &args.module_allowlist {
        allowlist.modules = AllowList::load(path)?;
    }

    if let Some(path) = &args.source_allowlist {
        allowlist.source_files = AllowList::load(path)?;
    }

    let loader = Loader::new();
    let recorded = CoverageRecorder::new(cmd)
        .allowlist(allowlist)
        .loader(loader)
        .timeout(timeout)
        .record()?;

    dump_modoff(&recorded.coverage)?;

    Ok(())
}

fn dump_modoff(coverage: &BinaryCoverage) -> Result<()> {
    for (module, coverage) in &coverage.modules {
        for (offset, count) in coverage.as_ref() {
            if count.reached() {
                println!("{}+{offset:x}", module.base_name());
            }
        }
    }

    Ok(())
}
