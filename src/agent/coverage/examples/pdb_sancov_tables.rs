// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::fs;

use anyhow::Result;
use coverage::block::pe_provider::PeSancovBasicBlockProvider;
use goblin::pe::PE;
use pdb::PDB;
use structopt::StructOpt;

#[derive(Debug, PartialEq, StructOpt)]
struct Opt {
    #[structopt(long)]
    pe: std::path::PathBuf,

    #[structopt(long)]
    pdb: Option<std::path::PathBuf>,
}

#[cfg(target_os = "windows")]
fn main() -> Result<()> {
    let opt = Opt::from_args();

    let data = fs::read(&opt.pe)?;
    let pe = PE::parse(&data)?;

    let pdb = opt
        .pdb
        .clone()
        .unwrap_or_else(|| opt.pe.with_extension("pdb"));
    let pdb = fs::File::open(pdb)?;
    let mut pdb = PDB::open(pdb)?;

    let mut provider = PeSancovBasicBlockProvider::new(&data, &pe, &mut pdb);
    let blocks = provider.provide()?;

    println!("blocks = {:x?}", blocks);

    Ok(())
}

#[cfg(target_os = "linux")]
fn main() {}
