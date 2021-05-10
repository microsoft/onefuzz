// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[structopt(short, long)]
    elf: std::path::PathBuf,

    #[structopt(short, long)]
    pcs: bool,

    #[structopt(short, long)]
    inline: bool,
}

#[cfg(target_os = "windows")]
fn main() -> Result<()> {
    Ok(())
}

#[cfg(target_os = "linux")]
fn main() -> Result<()> {
    use coverage::elf::{ElfContext, ElfSancovBasicBlockProvider};
    use goblin::elf::Elf;

    let opt = Opt::from_args();

    let data = std::fs::read(opt.elf)?;
    let elf = Elf::parse(&data)?;
    let ctx = ElfContext::new(&data, &elf)?;
    let mut provider = ElfSancovBasicBlockProvider::new(ctx);

    provider.set_check_pc_table(opt.pcs);

    let blocks = provider.provide()?;

    println!("block count = {}", blocks.len());
    println!("blocks = {:x?}", blocks);

    Ok(())
}
