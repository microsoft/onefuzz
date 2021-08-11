// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[macro_use]
extern crate clap;

use anyhow::{Context, Result};
use env_logger;
use srcview::{ModOff, Report, SrcLine, SrcView};
use std::fs::{self};
use std::io::{stdout, Write};
use std::path::PathBuf;
use structopt::StructOpt;

#[derive(StructOpt, Debug)]
enum Opt {
    DumpSrcloc(SrcLocOpt),
    DumpPdbPaths(PdbPathsOpt),
    DumpModoffs(ModOffOpt),
    DumpCobertuna(DumpCobertunaOpt),
    Licenses,
    Version,
}

#[derive(StructOpt, Debug)]
struct ModOffOpt {
    modoff_path: PathBuf,
}

#[derive(StructOpt, Debug)]
struct PdbPathsOpt {
    pdb_path: PathBuf,
}

#[derive(StructOpt, Debug)]
struct SrcLocOpt {
    pdb_path: PathBuf,
    modoff_path: PathBuf,
}

#[derive(StructOpt, Debug)]
struct DumpCobertunaOpt {
    pdb_path: PathBuf,
    modoff_path: PathBuf,
}

fn main() -> Result<()> {
    env_logger::init();

    let opt = Opt::from_args();

    match opt {
        Opt::DumpSrcloc(opts) => dump_srcloc(opts)?,
        Opt::DumpPdbPaths(opts) => dump_pdb_paths(opts)?,
        Opt::DumpModoffs(opts) => dump_mod_offs(opts)?,
        Opt::DumpCobertuna(opts) => dump_cobertura(opts)?,
        Opt::Licenses => licenses()?,
        Opt::Version => version(),
    };

    Ok(())
}

fn version() {
    println!("{}", crate_version!());
}

fn licenses() -> Result<()> {
    stdout().write_all(include_bytes!("../../../data/licenses.json"))?;
    Ok(())
}

fn dump_srcloc(opts: SrcLocOpt) -> Result<()> {
    let modoff_data = fs::read_to_string(&opts.modoff_path)
        .with_context(|| format!("unable to read modoff_path: {}", opts.modoff_path.display()))?;
    let modoffs = ModOff::parse(&modoff_data)?;
    let mut srcview = SrcView::new();
    srcview.insert("example.exe", &opts.pdb_path)?;

    for modoff in &modoffs {
        print!(" +{:04x} ", modoff.offset);
        match srcview.modoff(&modoff) {
            Some(srcloc) => println!("{}", srcloc),
            None => println!(""),
        }
    }

    Ok(())
}

fn dump_pdb_paths(opts: PdbPathsOpt) -> Result<()> {
    let mut srcview = SrcView::new();
    srcview.insert(&opts.pdb_path.to_string_lossy().to_string(), &opts.pdb_path)?;

    for path in srcview.paths() {
        println!("{}", path.display());
    }
    Ok(())
}

fn dump_mod_offs(opts: ModOffOpt) -> Result<()> {
    let modoff = fs::read_to_string(&opts.modoff_path)?;
    println!("{:#?}", ModOff::parse(&modoff).unwrap());
    Ok(())
}

fn dump_cobertura(opts: DumpCobertunaOpt) -> Result<()> {
    // read our modoff file and parse it to a vector
    let modoff_data = fs::read_to_string(&opts.modoff_path)?;
    let modoffs = ModOff::parse(&modoff_data)?;

    // create all the likely module base names -- do we care about mixed case
    // here?
    let bare = opts
        .pdb_path
        .file_stem()
        .expect("unable to identify file stem")
        .to_string_lossy();
    let exe = format!("{}.exe", bare);
    let dll = format!("{}.dll", bare);
    let sys = format!("{}.sys", bare);

    // create our new SrcView and insert our only pdb into it
    // we don't know what the modoff module will be, so create a mapping from
    // all likely names to the pdb

    let mut srcview = SrcView::new();

    // in theory we could refcount the pdbcache's to save resources here, but
    // Im not sure thats necesary...
    srcview.insert(&bare, &opts.pdb_path)?;
    srcview.insert(&exe, &opts.pdb_path)?;
    srcview.insert(&dll, &opts.pdb_path)?;
    srcview.insert(&sys, &opts.pdb_path)?;

    // Convert our ModOffs to SrcLine so we can draw it
    let coverage: Vec<SrcLine> = modoffs
        .into_iter()
        .filter_map(|m| srcview.modoff(&m))
        .collect();

    // Generate our report, filtering on our example path
    let r = Report::new(&coverage, &srcview, Some(r"E:\\1f\\coverage\\example"))?;

    // Format it as cobertura and display it
    let formatted = r.cobertura(Some(r"E:\\1f\\coverage\\"))?;
    println!("{}", formatted);

    Ok(())
}
